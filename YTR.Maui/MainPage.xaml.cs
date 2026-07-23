namespace YTR.Maui;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();

#if WINDOWS
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
