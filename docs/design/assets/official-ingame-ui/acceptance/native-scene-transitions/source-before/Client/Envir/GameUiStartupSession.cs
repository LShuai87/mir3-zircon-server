using System;
using System.IO;
using System.Threading;
using Client.Scenes;

namespace Client.Envir
{
    public enum GameUiStyle
    {
        Legacy,
        OfficialV1
    }

    // An application-lifetime lease created after D3D initialization and before scenes.
    // Scene installers may borrow Package; only this owner releases it at shutdown.
    // Preparing the selection alone never changes an existing scene or its controls.
    public sealed class GameUiStartupSession : IDisposable
    {
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        private GameUiVerifiedPackage package;
        private GameScene attachedScene;
        private GameUiLayoutStore layoutStore;
        internal string ConfigurationDirectory { get; }
        public GameUiStyle EffectiveStyle { get; private set; }
        public string Diagnostic { get; private set; }
        public bool RequiresRestart { get; private set; }
        internal bool CanInstallOfficial => !IsDisposed && !RequiresRestart && EffectiveStyle == GameUiStyle.OfficialV1;
        public bool IsDisposed { get; private set; }
        public GameUiVerifiedPackage Package
        {
            get
            {
                CheckOwner();
                return package;
            }
        }

        private GameUiStartupSession(GameUiStyle style, GameUiVerifiedPackage package, string diagnostic)
        {
            var configPath = (Library.ConfigPath)Attribute.GetCustomAttribute(typeof(Config), typeof(Library.ConfigPath));
            ConfigurationDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath.Path));
            EffectiveStyle = style;
            this.package = package;
            Diagnostic = diagnostic;
        }

        public static GameUiStartupSession Prepare(string requestedStyle, string applicationDirectory)
        {
            return Prepare(requestedStyle, applicationDirectory, GameUiOfficialV1Catalog.RuntimeReady);
        }

        // The gate seam is internal for isolated tests. The application entry point
        // always uses the compiled catalog gate; config cannot bypass acceptance.
        internal static GameUiStartupSession Prepare(string requestedStyle, string applicationDirectory, bool runtimeReady)
        {
            string value = requestedStyle?.Trim();
            if (string.Equals(value, nameof(GameUiStyle.Legacy), StringComparison.OrdinalIgnoreCase))
                return Legacy(null);
            if (!string.Equals(value, nameof(GameUiStyle.OfficialV1), StringComparison.OrdinalIgnoreCase))
                return Legacy("Invalid GameUiStyle; expected Legacy or OfficialV1. Using Legacy until restart.");
            if (!runtimeReady)
                return Legacy("OfficialV1 acceptance gate is closed. Using Legacy until restart.");

            GameUiVerifiedPackage verified;
            string diagnostic;
            string path = Path.Combine(applicationDirectory, GameUiOfficialV1Catalog.PackageRelativePath);
            if (!GameUiVerifiedPackage.TryLoad(path, GameUiOfficialV1Catalog.PackageManifest, out verified, out diagnostic))
                return Legacy("OfficialV1 package preflight failed. Using Legacy until restart. " + diagnostic);
            return new GameUiStartupSession(GameUiStyle.OfficialV1, verified, null);
        }

        private static GameUiStartupSession Legacy(string diagnostic)
        {
            return new GameUiStartupSession(GameUiStyle.Legacy, null, diagnostic);
        }

        // Registers the borrower before the first control mutation. A failed rollback
        // keeps this registration and package lease until scene disposal succeeds.
        internal void AttachScene(GameScene scene, string configurationDirectory)
        {
            CheckOwner();
            if (scene == null) throw new ArgumentNullException(nameof(scene));
            if (!CanInstallOfficial) throw new InvalidOperationException("This startup session cannot install OfficialV1.");
            if (attachedScene != null) throw new InvalidOperationException("Detach the previous scene before entering another scene.");
            attachedScene = scene;
            try
            {
                if (layoutStore == null) layoutStore = new GameUiLayoutStore(configurationDirectory);
                scene.InstallOfficialWindowCandidate(package, layoutStore);
                scene.RestoreOfficialChatLayout();
            }
            catch (Exception installError)
            {
                if (scene.HasOfficialWindowOwner)
                {
                    Fail(installError);
                    return;
                }
                try { scene.RestoreLegacyStartupLayout(); }
                catch (Exception restoreError)
                {
                    Fail(new AggregateException("Legacy startup restoration failed.", installError, restoreError));
                    return;
                }
                attachedScene = null;
                EffectiveStyle = GameUiStyle.Legacy;
                Diagnostic = "OfficialV1 scene installation failed and rolled back. Using Legacy until restart. " + installError;
                CEnvir.SaveError("Game UI startup: " + Diagnostic);
            }
        }

        internal void DetachScene(GameScene scene)
        {
            CheckOwner();
            if (!ReferenceEquals(attachedScene, scene)) return;
            if (scene.HasOfficialWindowOwner) throw new InvalidOperationException("Detach scene controls before releasing their package registration.");
            attachedScene = null;
        }

        internal bool HandleSceneFailure(GameScene scene, Exception error)
        {
            CheckOwner();
            if (RequiresRestart) return true;
            if (scene == null || !ReferenceEquals(attachedScene, scene)) return false;
            Fail(error);
            return true;
        }

        private void Fail(Exception error)
        {
            if (RequiresRestart) return;
            RequiresRestart = true; // Set before cleanup or notification can raise events.
            Diagnostic = "OfficialV1 UI failed. Close and restart the client. " + error;
            attachedScene?.StopGameUiOperations();
            CEnvir.Shift = CEnvir.Alt = CEnvir.Ctrl = false;
            CEnvir.SaveError("Game UI failure: " + Diagnostic);
            CEnvir.Target?.ShowGameUiFailureNotice();
        }

        private void CheckOwner()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("The UI startup session belongs to the startup/render thread.");
            if (IsDisposed) throw new ObjectDisposedException(nameof(GameUiStartupSession));
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            CheckOwner();
            if (attachedScene != null) throw new InvalidOperationException("Dispose the borrowing game scene before releasing its UI startup session.");
            try { package?.Dispose(); }
            finally { package = null; IsDisposed = true; }
        }
    }
}
