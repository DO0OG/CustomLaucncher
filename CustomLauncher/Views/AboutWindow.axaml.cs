using System.Reflection;
using Avalonia.Controls;

namespace CustomLauncher.Views;
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        // InformationalVersion 뒤의 +커밋해시는 잘라낸다.
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        VersionText.Text = $"버전 {version?.Split('+')[0]}";
    }
}
