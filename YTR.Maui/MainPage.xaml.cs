namespace YTR.Maui;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();

#if WINDOWS
		blazorWebView.BlazorWebViewInitializing += (sender, args) =>
		{
			// Set WebView2 user data folder to a writable location.
			// Without this, WebView2 fails silently when the app is installed
			// to a read-only directory like Program Files.
			args.EnvironmentOptions = new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions();
			args.UserDataFolder = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"YTR", "WebView2");
		};

		blazorWebView.BlazorWebViewInitialized += (sender, args) =>
		{
#if DEBUG
			args.WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
			args.WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
#endif
		};
#endif
	}
}
