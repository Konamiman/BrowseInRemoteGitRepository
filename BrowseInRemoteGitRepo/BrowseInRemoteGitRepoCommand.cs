using System;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Konamiman.BrowseInRemoteGitRepo
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class BrowseInRemoteGitRepoCommand
    {
        const string configKey_BrowseCommandFormatString = "Konamiman.BrowseInRemoteGitRepo.BrowseCommandTemplate";
        const string configKey_BaseUrl = "Konamiman.BrowseInRemoteGitRepo.BaseUrl";

        /// <summary>
        /// Command IDs.
        /// </summary>
        public const int BrowseCommandId = 0x0100;
        public const int CopyCommandId = 0x0101;
        public const int BrowseCommandId_se = 0x0102;
        public const int CopyCommandId_se = 0x0103;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("583a97cb-eca1-494d-bec4-bf1095cd274b");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly Package package;

        /// <summary>
        /// Initializes a new instance of the <see cref="BrowseInRemoteGitRepoCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        private BrowseInRemoteGitRepoCommand(Package package)
        {
            if(package == null) {
                throw new ArgumentNullException(nameof(package));
            }

            this.package = package;

            var commandService = this.ServiceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService == null) return;

            var browseMenuCommandID = new CommandID(CommandSet, BrowseCommandId);
            var browseMenuItem = new OleMenuCommand(BrowseMenuItemCallback, browseMenuCommandID);
            browseMenuItem.BeforeQueryStatus += (sender, e) => MenuItemOnBeforeQueryStatus(sender, true);
            commandService.AddCommand(browseMenuItem);

            var copyMenuCommandID = new CommandID(CommandSet, CopyCommandId);
            var copyMenuItem = new OleMenuCommand(CopyMenuItemCallback, copyMenuCommandID);
            copyMenuItem.BeforeQueryStatus += (sender, e) => MenuItemOnBeforeQueryStatus(sender, true);
            commandService.AddCommand(copyMenuItem);

            //These are for solution explorer

            var browseMenuCommandID_se = new CommandID(CommandSet, BrowseCommandId_se);
            var browseMenuItem_se = new OleMenuCommand(BrowseMenuItemCallback, browseMenuCommandID_se);
            browseMenuItem_se.BeforeQueryStatus += (sender, e) => MenuItemOnBeforeQueryStatus(sender, false);
            commandService.AddCommand(browseMenuItem_se);

            var copyMenuCommandID_se = new CommandID(CommandSet, CopyCommandId_se);
            var copyMenuItem_se = new OleMenuCommand(CopyMenuItemCallback, copyMenuCommandID_se);
            copyMenuItem_se.BeforeQueryStatus += (sender, e) => MenuItemOnBeforeQueryStatus(sender, false);
            commandService.AddCommand(copyMenuItem_se);
        }

        //http://www.diaryofaninja.com/blog/2014/02/18/who-said-building-visual-studio-extensions-was-hard
        private void MenuItemOnBeforeQueryStatus(object sender, bool showLineNumber)
        {
            fileName = null;

            // get the menu that fired the event
            var menuCommand = sender as OleMenuCommand;
            if(menuCommand == null)
                return;

            // start by assuming that the menu will not be shown
            menuCommand.Visible = false;
            menuCommand.Enabled = false;

            IVsHierarchy hierarchy = null;
            uint itemid = VSConstants.VSITEMID_NIL;

            if(!IsSingleProjectItemSelection(out hierarchy, out itemid))
                return;

            // Get the file path
            string itemFullPath = null;
            ((IVsProject)hierarchy).GetMkDocument(itemid, out itemFullPath);
            var transformFileInfo = new FileInfo(itemFullPath);
            fileName = transformFileInfo.FullName;

            if (string.IsNullOrWhiteSpace(fileName))
                return;

            filePath = Path.GetDirectoryName(fileName);

            try {
                RunGitCommand("-C . rev-parse");
            }
            catch(GitException) {
                return; //assume no Git repo
            }
            catch(Exception ex) {
                Show($"Error when invoking git:\r\n\r\n{ex.Message}\r\n\r\n(is the location of git.exe in PATH?)");
                return;
            }

            line = -1;
            if(showLineNumber) {
                var textView = GetIVsTextView(fileName);
                if (textView != null) {
                    int column;
                    textView.GetCaretPos(out line, out column);
                }
            }

            menuCommand.Visible = true;
            menuCommand.Enabled = true;
        }

        private string fileName;
        private string filePath;
        private int line;
        private string lastGitCommandExecuted;

        //http://www.diaryofaninja.com/blog/2014/02/18/who-said-building-visual-studio-extensions-was-hard
        public static bool IsSingleProjectItemSelection(out IVsHierarchy hierarchy, out uint itemid)
        {
            hierarchy = null;
            itemid = VSConstants.VSITEMID_NIL;

            var monitorSelection = Package.GetGlobalService(typeof(SVsShellMonitorSelection)) as IVsMonitorSelection;
            var solution = Package.GetGlobalService(typeof(SVsSolution)) as IVsSolution;
            if(monitorSelection == null || solution == null) {
                return false;
            }

            IVsMultiItemSelect multiItemSelect = null;
            IntPtr hierarchyPtr = IntPtr.Zero;
            IntPtr selectionContainerPtr = IntPtr.Zero;

            try {
                var hr = monitorSelection.GetCurrentSelection(out hierarchyPtr, out itemid, out multiItemSelect, out selectionContainerPtr);

                if(ErrorHandler.Failed(hr) || hierarchyPtr == IntPtr.Zero || itemid == VSConstants.VSITEMID_NIL) {
                    // there is no selection
                    return false;
                }

                // multiple items are selected
                if(multiItemSelect != null)
                    return false;

                // there is a hierarchy root node selected, thus it is not a single item inside a project

                if(itemid == VSConstants.VSITEMID_ROOT)
                    return false;

                hierarchy = Marshal.GetObjectForIUnknown(hierarchyPtr) as IVsHierarchy;
                if(hierarchy == null)
                    return false;

                Guid guidProjectID = Guid.Empty;

                if(ErrorHandler.Failed(solution.GetGuidOfProject(hierarchy, out guidProjectID))) {
                    return false; // hierarchy is not a project inside the Solution if it does not have a ProjectID Guid
                }

                // if we got this far then there is a single project item selected
                return true;
            }
            finally {
                if(selectionContainerPtr != IntPtr.Zero) {
                    Marshal.Release(selectionContainerPtr);
                }

                if(hierarchyPtr != IntPtr.Zero) {
                    Marshal.Release(hierarchyPtr);
                }
            }
        }

        //http://stackoverflow.com/a/2427368/4574
        internal static Microsoft.VisualStudio.TextManager.Interop.IVsTextView GetIVsTextView(string filePath)
        {
            var dte2 = (EnvDTE80.DTE2)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(Microsoft.VisualStudio.Shell.Interop.SDTE));
            Microsoft.VisualStudio.OLE.Interop.IServiceProvider sp = (Microsoft.VisualStudio.OLE.Interop.IServiceProvider)dte2;
            Microsoft.VisualStudio.Shell.ServiceProvider serviceProvider = new Microsoft.VisualStudio.Shell.ServiceProvider(sp);

            Microsoft.VisualStudio.Shell.Interop.IVsUIHierarchy uiHierarchy;
            uint itemID;
            Microsoft.VisualStudio.Shell.Interop.IVsWindowFrame windowFrame;
            //Microsoft.VisualStudio.Text.Editor.IWpfTextView wpfTextView = null;
            if(Microsoft.VisualStudio.Shell.VsShellUtilities.IsDocumentOpen(serviceProvider, filePath, Guid.Empty,
                                            out uiHierarchy, out itemID, out windowFrame)) {
                // Get the IVsTextView from the windowFrame.
                return Microsoft.VisualStudio.Shell.VsShellUtilities.GetTextView(windowFrame);
            }

            return null;
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static BrowseInRemoteGitRepoCommand Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private IServiceProvider ServiceProvider => this.package;

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static void Initialize(Package package)
        {
            Instance = new BrowseInRemoteGitRepoCommand(package);
        }

        private void BrowseMenuItemCallback(object sender, EventArgs e)
        {
            MenuItemCallback(sender, e, StartProcess, true);
        }

        private void CopyMenuItemCallback(object sender, EventArgs e)
        {
            MenuItemCallback(sender, e, Clipboard.SetText, false);
        }

        private void StartProcess(string fullUrl)
        {
            string fullCommand = fullUrl;
            try
            {
                var browseCommandFormatString =
                    RunGitCommand($"config --get {configKey_BrowseCommandFormatString}");
                if (browseCommandFormatString == null) {
                    Process.Start(fullUrl);
                    return;
                }

                fullCommand = string.Format(browseCommandFormatString, fullUrl);

                string command;
                string arguments;
                if (fullCommand.StartsWith("\"")) {
                    var indexOfLastQuote = fullCommand.IndexOf("\"", 1);
                    if (indexOfLastQuote == -1) {
                        throw new Exception(
                            $"The config value for {configKey_BrowseCommandFormatString} has starting quote, but no ending quote");
                    }

                    command = fullCommand.Substring(1, indexOfLastQuote - 1);
                    arguments = fullCommand.Substring(indexOfLastQuote + 1);
                }
                else {
                    var indexOfSpace = fullCommand.IndexOf(" ");
                    if (indexOfSpace == -1)
                    {
                        command = fullCommand;
                        arguments = "";
                    }
                    else
                    {
                        command = fullCommand.Substring(0, indexOfSpace);
                        arguments = fullCommand.Substring(indexOfSpace);
                    }
                }

                Process.Start(command, arguments);
            }
            catch(Win32Exception ex) { 
                Show($"Error when executing browse command\r\n{fullCommand}\r\n\r\n{ex.Message}");
            }
            catch(Exception ex) { 
                Show($"Error when preparing browse command for execution:\r\n\r\n{ex.Message}");
            }
        }

        private void MenuItemCallback(object sender, EventArgs e, Action<string> processUrl, bool validateUrl)
        {
            if(fileName == null) {
                Show("Error: no file selected");
                return;
            }

            try {
                string branch;
                try {
                    branch = RunGitCommand("branch")
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Single(x => x.StartsWith("*"))
                        .Substring(2);
                }
                catch(Exception ex) when(!(ex is GitException)) {
                    Show("Unexpected output from 'git branch'");
                    return;
                }

                var baseUrl = 
                    RunGitCommand($"config --get {configKey_BaseUrl}") ??
                    RunGitCommand("config --get remote.origin.url");

                if(baseUrl == null) {
                    Show($"There's no remote (remote.origin.url) nor manual base URL ({configKey_BaseUrl}) configured for this repository");
                    return;
                }

                baseUrl = baseUrl.TrimEnd('/');

                if(baseUrl.EndsWith(".git"))
                    baseUrl = baseUrl.Substring(0, baseUrl.Length - 4);

                if (baseUrl.StartsWith("git@")) {
                    // Try converting this from an SSH url to an https url.
                    // For example, this transforms "git@github.com:Konamiman/BrowseInRemoteGitRepository" into
                    // "https://github.com/Konamiman/BrowseInRemoteGitRepository".
                    // Maybe there should be a config option for http vs https, but for now https seems like the better default.

                    var colonIndex = baseUrl.IndexOf(':');
                    if (colonIndex != -1 && colonIndex + 1 < baseUrl.Length)
                        baseUrl = "https://" + baseUrl.Substring(4, colonIndex - 4) + "/" + baseUrl.Substring(colonIndex + 1);
                }


                var baseLocalRepoRoot = RunGitCommand("rev-parse --show-toplevel");

                if (baseUrl.Contains("{0}"))
                    baseUrl = string.Format(baseUrl, Path.GetFileName(baseLocalRepoRoot));

                var gittedFilename = fileName.Replace('\\', '/').Replace(baseLocalRepoRoot, "").Trim('/');
                var escapedGittedFilename = gittedFilename.Replace(@"/", @"\/");
                gittedFilename = RunGitCommand($"ls-files | findstr -I {escapedGittedFilename}", baseLocalRepoRoot);

                var fullUrl = baseUrl + "/blob/" + branch + "/" + gittedFilename;
                if (line != -1)
                    fullUrl += "#L" + (line + 1);

                if(validateUrl && 
                    !fullUrl.StartsWith("http://", StringComparison.InvariantCultureIgnoreCase) &&
                    !fullUrl.StartsWith("https://", StringComparison.InvariantCultureIgnoreCase)) {
                        Show(
$@"The remote URL doesn't seem to be browsable (doesn't start with 'http[s]://'):

{fullUrl}

You can configure a browsable base URL for the repository by running:
git config {configKey_BaseUrl} <base URL>");
                        return;
                    }

                processUrl(fullUrl);
            }
            catch (GitException ex)
            {
                Show($"Error when executing 'git {lastGitCommandExecuted}':\r\n\r\n{ex.Message}");
            }
        }

        private void Show(string message)
        {
            VsShellUtilities.ShowMessageBox(
                this.ServiceProvider,
                message,
                "Browse in remote Git repository:",
                OLEMSGICON.OLEMSGICON_WARNING,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        //http://stackoverflow.com/a/6119394/4574
        private string RunGitCommand(string command, string workingDirectory = null)
        {
            lastGitCommandExecuted = command;

            var gitInfo = new ProcessStartInfo
            {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                FileName = "cmd.exe",
                Arguments = $@"/C ""git {command}""",
                UseShellExecute = false
            };

            var gitProcess = new Process();
            //gitInfo.Arguments = command;
            gitInfo.WorkingDirectory = workingDirectory ?? filePath;

            gitProcess.StartInfo = gitInfo;
            gitProcess.Start();

            var error = gitProcess.StandardError.ReadToEnd();  // pick up STDERR
            var output = gitProcess.StandardOutput.ReadToEnd(); // pick up STDOUT

            gitProcess.WaitForExit();
            gitProcess.Close();

            if(!string.IsNullOrEmpty(error))
                throw new GitException(error);

            return output == "" ? null : output.Trim('\r', '\n', ' ');
        }
    }
}
