using System;

namespace Konamiman.BrowseInRemoteGitRepo
{
    public class GitException : Exception
    {
        public GitException(string message) : base(message)
        {
        }
    }
}
