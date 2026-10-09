using System;

namespace SpineRecovery.App
{
    public sealed class AppState
    {
        public AppRoute Route { get; private set; } = AppRoute.Login;
        public UserSession Session { get; private set; }

        public event Action<AppRoute> RouteChanged;
        public event Action<UserSession> SessionChanged;

        public void Navigate(AppRoute route)
        {
            if (Route == route)
            {
                return;
            }

            Route = route;
            RouteChanged?.Invoke(route);
        }

        public void SetSession(UserSession session)
        {
            Session = session;
            SessionChanged?.Invoke(session);
        }

        public void ClearSession()
        {
            Session = null;
            Navigate(AppRoute.Login);
            SessionChanged?.Invoke(null);
        }
    }
}
