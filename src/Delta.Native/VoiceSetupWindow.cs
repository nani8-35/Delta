using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using NAudio.CoreAudioApi;

namespace Delta.Native;
public sealed class VoiceSetupWindow : Window
{
    private sealed record OutputChoice(string Name, string? Id) { public override string ToString() => Name; }
    public VoicePreferences Preferences { get; }
    private readonly TextBox keyPath = new() { IsReadOnly = true, Margin = new Thickness(0, 6, 0, 6) };
    private readonly ComboBox modes = new() { Margin = new Thickness(0,4,0,8), MinHeight = 28 };
    private readonly CheckBox autoSend = new() { Content = "Send after 1 second of silence", Margin = new Thickness(0,4,0,8) };
    private readonly ComboBox outputs = new() { Margin = new Thickness(0, 6, 0, 10), MinHeight = 30 };
    private readonly TextBlock notice = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,8) };
    public VoiceSetupWindow(VoicePreferences initial)
    {
        WindowsServices.ProtectWindow(this);
        Preferences = new() { KeyFile = initial.ResolveKeyFile(AppContext.BaseDirectory), OutputDeviceId = initial.OutputDeviceId, LiveDictation = initial.LiveDictation, AutoSend = initial.AutoSend };
        Title = "Delta — system audio setup"; Width = 560; Height = Math.Min(650, SystemParameters.WorkArea.Height - 32); MinWidth = 420; MinHeight = 360; ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Background = System.Windows.Media.Brushes.Black; Foreground = System.Windows.Media.Brushes.White;
        var panel = new StackPanel { Margin = new Thickness(20) }; Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        panel.Children.Add(new TextBlock { Text = "Voice listens to system playback only", FontSize = 18 });
        panel.Children.Add(new TextBlock { Text = "Choose the speakers or headphones playing the audio. Delta does not open a microphone.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,12) });
        panel.Children.Add(new TextBlock { Text = "Google AI Studio key file" });
        keyPath.Text = Preferences.KeyFile ?? ""; panel.Children.Add(keyPath);
        panel.Children.Add(new TextBlock { Text = "Select a plain-text file containing only your Gemini / Google AI Studio key. No need to share the key in chat.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,8) });
        var browse = new Button { Content = "Select API.txt…" }; browse.Click += Browse; panel.Children.Add(browse);
        panel.Children.Add(new TextBlock { Text = "System audio output device", Margin = new Thickness(0,12,0,0) }); panel.Children.Add(outputs);
        outputs.Items.Add(new OutputChoice("Windows default playback device", null)); outputs.SelectedIndex = 0;
        try {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)) {
                using (device) {
                    var item = new OutputChoice(device.FriendlyName, device.ID); outputs.Items.Add(item);
                    if (item.Id == initial.OutputDeviceId) outputs.SelectedItem = item;
                }
            }
            if (initial.OutputDeviceId != null && outputs.SelectedIndex == 0)
                notice.Text = "The previous output device is unavailable. Choose the device currently playing audio.";
        } catch { notice.Text = "Windows could not list playback devices. Check your speakers/headphones and retry."; }
        panel.Children.Add(new TextBlock { Text = "Transcription mode" });
        modes.Items.Add("Live dictation — streaming");
        modes.Items.Add("Request mode — 1-second batches");
        modes.SelectedIndex = initial.LiveDictation ? 0 : 1; panel.Children.Add(modes);
        autoSend.IsChecked = initial.AutoSend; panel.Children.Add(autoSend);
        panel.Children.Add(new TextBlock { Text = "Live words appear directly in the selected chat input. Automatic send waits for 1 second of quiet and final recognition. Listening stays ON until you turn it OFF. Speed depends on Google and the connection.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(notice);
        var save = new Button { Content = "Save setup", IsDefault = true }; save.Click += Save; panel.Children.Add(save);
        var cancel = new Button { Content = "Cancel", IsCancel = true }; panel.Children.Add(cancel);
    }
    private void Browse(object sender, RoutedEventArgs args)
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Title = "Select your Google AI Studio key file", Filter = "Key files (*.txt;*.json)|*.txt;*.json|All files (*.*)|*.*", CheckFileExists = true };
        if (picker.ShowDialog(this) != true) return;
        keyPath.Text = picker.FileName; notice.Text = "File selected. Save setup to validate it.";
    }
    internal string ValidationMessage => notice.Text;
    private void Save(object sender, RoutedEventArgs args) => SaveSelection();
    internal bool SaveSelection()
    {
        try {
            _ = ApiSettings.LoadFile(keyPath.Text);
            Preferences.KeyFile = Path.GetFullPath(keyPath.Text);
            Preferences.OutputDeviceId = (outputs.SelectedItem as OutputChoice)?.Id;
            Preferences.LiveDictation = modes.SelectedIndex == 0; Preferences.AutoSend = autoSend.IsChecked == true;
            Preferences.Save(App.DataFolder); DialogResult = true; return true;
        } catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { notice.Text = ex.Message; return false; }
    }
}




