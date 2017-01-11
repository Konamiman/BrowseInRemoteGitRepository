# Browse in remote Git repository #

This is a simple Visual Studio extension that adds two entries, _"Browse in remote repository"_ and _"Copy URL of remote repository version"_, to the context menu when right clicking a file in solution Explorer, or in the code editor for open files, provided that the solution lives in a Git repository with a configured remote. This is useful when you are coding collaboratively and want to point a specific piece of code to another person.

The format of the generated URL is `<remote base URL>/blob/<branch name>/<file path and name>`. When clicking in the code editor, `#L<line number>` is added as well. This format is compatible with [GitHub](http://github.com) and [GitLab](http://gitlab.com).

### Requirements ###

* `git.exe` location must be in the PATH variable.
* The repository must have a configured remote, or the base URL must have been configured manually (see below).
* Obvious, but: the _"Browse in remote repository"_ requires a web browser to be installed.
* Also should be obvious but easy to overlook: for _"Browse in remote repository"_, if the remote repository is private you must have been granted access to it, and you must have a session open in the browser that the command will open (see below).

### Configuring the remote base URL ###

By default the base URL for the remote repository will be obtained by executing `git config --get remote.origin.url`. For the cases where this does not work as expected (the schema for the remote URL is not `http[s]://` or there is actually no remote configured for the local repository, for example), it is possible to manually set the base url by executing the following in the local repository: `git config Konamiman.BrowseInRemoteGitRepo.BaseUrl <base URL>`.

If the supplied value includes the token `{0}`, it will be replaced with the last part of the local repository directory name (so `xyz` for `c:\abc\def\xyz`). This may be useful if you have all your projects under the same account of the same provider and want to set a global value for the setting (add `--global` when creating the setting for this).

### Configuring the command executed for browsing ###

The default action of the _"Browse in remote repository"_ command is to open the full remote URL of the file in the default browser. If you want a different behavior (such as using a different browser or adding extra command line options) you can do so by executing `git config Konamiman.BrowseInRemoteGitRepo.BrowseCommandTemplate <command  template>`, you must add a `{0}` token to the template that will be replaced with the remote URL of the file. Again, add `--global` if necessary.

For example, if you want the command to always open Firefox, run the following: `git config --global Konamiman.BrowseInRemoteGitRepo.BrowseCommandTemplate "\"C:\Program Files (x86)\Mozilla Firefox\firefox.exe\" {0}"`

### Finally... ###

This is my first Visual Studio extension, so please forgive all the rough edges. Suggestions/Pull Requests are more than welcome.

By the way, I am [this guy](http://stackoverflow.com/users/4574/konamiman?tab=profile) and [my kids are shoe slayers](http://www.konamiman.com/msx/msx-e.html#donate).