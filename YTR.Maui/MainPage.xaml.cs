namespace YTR.Maui;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();

#if DEBUG
		blazorWebView.BlazorWebViewInitialized += (sender, args) =>
		{
#if WINDOWS
			args.WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
			args.WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
#endif
		};
#endif
	}
}
