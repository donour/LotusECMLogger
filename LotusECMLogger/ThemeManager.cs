namespace LotusECMLogger
{
    /// <summary>
    /// Centralizes application-wide light/dark theming built on the .NET 10
    /// <see cref="Application.SetColorMode"/> API. Keeping the (still experimental)
    /// API call in one place confines the WFO5001 suppression to a single file.
    /// </summary>
    internal static class ThemeManager
    {
        /// <summary>
        /// Applies the requested color mode to the entire application.
        /// </summary>
        /// <remarks>
        /// Best applied once at startup, before any window is shown. Calling it at runtime
        /// re-themes newly created and repainted UI, but some controls only fully restyle
        /// after the application is restarted.
        /// </remarks>
        public static void Apply(bool darkMode)
        {
#pragma warning disable WFO5001 // Application.SetColorMode is an experimental .NET API.
            Application.SetColorMode(darkMode ? SystemColorMode.Dark : SystemColorMode.Classic);
#pragma warning restore WFO5001
        }
    }
}
