using System.Collections.Generic;
using UnityEngine;

namespace SpineRecovery.App
{
    public sealed class AppRouter : MonoBehaviour
    {
        private readonly Dictionary<AppRoute, GameObject> panels = new Dictionary<AppRoute, GameObject>();
        private AppState state;

        public void Initialize(AppState appState)
        {
            if (state != null)
            {
                state.RouteChanged -= HandleRouteChanged;
            }

            state = appState;
            state.RouteChanged += HandleRouteChanged;
            HandleRouteChanged(state.Route);
        }

        public void RegisterPanel(AppRoute route, GameObject panel)
        {
            panels[route] = panel;
            if (state != null)
            {
                HandleRouteChanged(state.Route);
            }
        }

        public void Navigate(AppRoute route)
        {
            if (state == null)
            {
                Debug.LogWarning("AppRouter is not initialized.");
                return;
            }

            state.Navigate(route);
        }

        private void HandleRouteChanged(AppRoute route)
        {
            foreach (KeyValuePair<AppRoute, GameObject> entry in panels)
            {
                if (entry.Value != null)
                {
                    entry.Value.SetActive(entry.Key == route);
                }
            }
        }

        private void OnDestroy()
        {
            if (state != null)
            {
                state.RouteChanged -= HandleRouteChanged;
            }
        }
    }
}
