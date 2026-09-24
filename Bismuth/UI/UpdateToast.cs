using PrismLib.UI;

namespace Bismuth.UI
{
    /* Bismuth's update toast. The card is PrismLib.UI.Toast, the same one Sapphire draws; what
       stays here is what only Bismuth can answer — what UpdateChecker is doing and what clicking
       it does.

       It replaces the centre-screen UpdatePopup as the "there's an update" notifier. The popup
       type stays: UpdateChecker still pushes status into it, harmlessly when it isn't open, and
       LogViewer and the duplicate-install prompt are built from its widget factories.

       PrismLib.UI ships in the mod folder rather than installing itself, because the UI half needs
       no shared instance across mods: each mod draws its own card and ToastStack keeps the two
       from overlapping by GameObject name. Unlike PrismBridge, nothing here needs a guard.

       State is polled, not subscribed: the checker fetches on the thread pool and Unity objects
       may only be touched from this one. */
    internal static class UpdateToast
    {
        private static Toast _toast;

        internal static void Tick()
        {
            if (!UpdateChecker.Ready) return;

            if (_toast == null)
            {
                _toast = new Toast("Bismuth");
                _toast.Theme.Text = Theme.Text;
                _toast.Theme.TextMuted = Theme.TextMuted;
                _toast.Theme.CloseHover = Theme.CloseHover;
                _toast.Theme.Background = new UnityEngine.Color(0.10f, 0.10f, 0.13f, 0.96f);
            }
            _toast.Theme.Accent = Theme.Accent;     // re-skinned at runtime by ApplyAccent
            _toast.Set(Current());
            _toast.Tick();
        }

        // What SHOULD be on screen; null = nothing. Key doubles as the dismiss key, so dismissing
        // "update available" doesn't also suppress the later "installed" for the same version.
        private static ToastContent Current()
        {
            switch (UpdateChecker.Status)
            {
                case UpdateChecker.State.Available:
                    string tag = UpdateChecker.LatestTag;
                    if (string.IsNullOrEmpty(tag)) return null;
                    return new ToastContent
                    {
                        Key = "avail:" + tag,
                        Title = Loc.T("Update available") + ": " + tag,
                        Hint = Loc.T("Click to update"),
                        AutoHide = true,            // only the actionable card times out
                        OnClick = UpdateChecker.InstallLatest,
                    };

                // No progress figure to show: the checker downloads the zip in one shot off the
                // thread pool, so the bar would sit at zero and lie.
                case UpdateChecker.State.Installing:
                    return new ToastContent
                    {
                        Key = "installing",
                        Title = Loc.T("Downloading update…"),
                        Hint = Loc.T("Please wait"),
                    };

                case UpdateChecker.State.Installed:
                    return new ToastContent
                    {
                        Key = "installed:" + UpdateChecker.StatusMessage,
                        Title = Loc.T("Update installed"),
                        Hint = Loc.T("Restart the game to apply it"),
                        Width = 400f,
                    };

                case UpdateChecker.State.Failed:
                    return new ToastContent
                    {
                        Key = "failed:" + UpdateChecker.StatusMessage,
                        Title = Loc.T("Update failed"),
                        Hint = UpdateChecker.StatusMessage,
                        OnClick = UpdateChecker.CheckNow,
                    };

                default:
                    return null;    // Idle / Checking / UpToDate say nothing
            }
        }

        internal static void Dispose()
        {
            if (_toast != null) _toast.Dispose();
            _toast = null;
        }
    }
}
