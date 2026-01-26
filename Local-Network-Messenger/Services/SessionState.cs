using Local_Network_Messenger.Models;

namespace Local_Network_Messenger.Services
{
    public sealed class SessionState
    {
        public UserProfile? CurrentUser { get; private set; }

        public bool IsAuthenticated => CurrentUser != null;

        public void SetUser(UserProfile user)
        {
            CurrentUser = user;
        }

        public void Clear()
        {
            CurrentUser = null;
        }
    }
}
