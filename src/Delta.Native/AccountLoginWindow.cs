using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Delta.Native;
public sealed class AccountLoginWindow : Window {
 readonly TextBox username=new(){Height=44,FontSize=15,VerticalContentAlignment=VerticalAlignment.Center};
 readonly PasswordBox password=new(){Height=44,FontSize=15,Padding=new Thickness(12,6,12,6),Background=ColorBrush("#101F33"),Foreground=Brushes.White,BorderBrush=ColorBrush("#426384")};
 readonly TextBox visiblePassword=new(){Height=44,FontSize=15,VerticalContentAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed};
 readonly CheckBox showPassword=new(){Content="Show Password",Foreground=ColorBrush("#CADCED"),Style=new Style(typeof(CheckBox)),Margin=new Thickness(112,10,0,0)};
 readonly TextBlock notice=new(){TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,MinHeight=36,Foreground=ColorBrush("#FFD6AD"),Margin=new Thickness(0,8,0,8)};
 readonly Button submit=new(){Content="Sign In",Width=240,Height=48,FontSize=17,FontWeight=FontWeights.SemiBold,Background=ColorBrush("#299AEF"),Foreground=Brushes.White,IsDefault=true};
 readonly CancellationTokenSource stop=new();
 static SolidColorBrush ColorBrush(string color)=>new((Color)ColorConverter.ConvertFromString(color));
 public AccountLoginWindow(AccountClient account,string message="") {
  ShowInTaskbar=false; Topmost=true; ShowActivated=true;
  Title="Delta Browser - Sign in";Width=720;Height=Math.Min(700,SystemParameters.WorkArea.Height-28);
  WindowStyle=WindowStyle.None;AllowsTransparency=true;ResizeMode=ResizeMode.NoResize;Opacity=.5;
  WindowStartupLocation=WindowStartupLocation.CenterScreen;Background=Brushes.Transparent;Foreground=Brushes.White;
  WindowsServices.ProtectWindow(this);
  var frame=new Border{CornerRadius=new CornerRadius(9),BorderThickness=new Thickness(1),BorderBrush=ColorBrush("#385574"),Background=new LinearGradientBrush(Color.FromRgb(17,30,49),Color.FromRgb(22,57,89),40)};Content=frame;
  var root=new Grid();frame.Child=root;root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(46)});root.RowDefinitions.Add(new RowDefinition());
  var header=new Grid{Background=ColorBrush("#142339")};root.Children.Add(header);
  header.MouseLeftButtonDown+=(_,e)=>{if(e.OriginalSource is Grid||e.OriginalSource is TextBlock){if(e.ClickCount==2)ToggleMaximize();else if(e.LeftButton==MouseButtonState.Pressed)DragMove();}};
  header.Children.Add(new TextBlock{Text="△  Delta Browser",FontSize=14,Foreground=ColorBrush("#C1D2E4"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(20,0,0,0)});
  var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,12,8)};header.Children.Add(buttons);
  var maximize=new Button{Content="□",Width=28,Padding=new Thickness(0),ToolTip="Maximize / restore"};maximize.Click+=(_,_)=>ToggleMaximize();buttons.Children.Add(maximize);
  var close=new Button{Content="×",Width=28,Padding=new Thickness(0),ToolTip="Close Delta"};close.Click+=(_,_)=>Close();buttons.Children.Add(close);
  var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroll,1);root.Children.Add(scroll);
  var panel=new StackPanel{MaxWidth=560,Margin=new Thickness(40,26,40,26),HorizontalAlignment=HorizontalAlignment.Stretch};scroll.Content=panel;
  panel.Children.Add(new Image{Source=BitmapFrame.Create(new Uri(Path.Combine(AppContext.BaseDirectory,"Assets","icon.ico"))),Width=68,Height=68,Margin=new Thickness(0,0,0,14)});
  panel.Children.Add(new TextBlock{Text="Delta Browser",FontSize=30,FontWeight=FontWeights.Bold,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,30)});
  panel.Children.Add(Field("Username:",Watermark(username,"Enter username")));
  var passwordGrid=new Grid();passwordGrid.Children.Add(password);passwordGrid.Children.Add(visiblePassword);
  var hint=new TextBlock{Text="Enter password",Foreground=ColorBrush("#738EA9"),FontSize=15,Margin=new Thickness(13,0,0,0),VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};passwordGrid.Children.Add(hint);
  void UpdateHint()=>hint.Visibility=(showPassword.IsChecked==true?visiblePassword.Text.Length:password.Password.Length)==0?Visibility.Visible:Visibility.Collapsed;
  password.PasswordChanged+=(_,_)=>UpdateHint();visiblePassword.TextChanged+=(_,_)=>UpdateHint();
  panel.Children.Add(Field("Password:",passwordGrid));panel.Children.Add(showPassword);
  showPassword.Checked+=(_,_)=>{visiblePassword.Text=password.Password;password.Visibility=Visibility.Collapsed;visiblePassword.Visibility=Visibility.Visible;UpdateHint();};
  showPassword.Unchecked+=(_,_)=>{password.Password=visiblePassword.Text;visiblePassword.Clear();visiblePassword.Visibility=Visibility.Collapsed;password.Visibility=Visibility.Visible;UpdateHint();};
  notice.Text=message;panel.Children.Add(notice);panel.Children.Add(submit);
  var help=new TextBlock{TextAlignment=TextAlignment.Center,Foreground=ColorBrush("#A4BCD5"),Margin=new Thickness(0,26,0,0),TextWrapping=TextWrapping.Wrap};help.Inlines.Add(new Run("Need help? "));
  var support=new Hyperlink(new Run("delta@acranix.com")){Foreground=ColorBrush("#93CDFF")};support.Click+=(_,_)=>MainWindow.OpenSupport();help.Inlines.Add(support);panel.Children.Add(help);
  panel.Children.Add(new TextBlock{Text="Your tools. A little more room to think.",FontSize=15,FontStyle=FontStyles.Italic,Foreground=ColorBrush("#8CA9C6"),TextAlignment=TextAlignment.Center,Margin=new Thickness(0,22,0,0)});
  Closed+=(_,_)=>{stop.Cancel();password.Clear();visiblePassword.Clear();};
  submit.Click+=async(_,_)=>{
   string enteredPassword=showPassword.IsChecked==true?visiblePassword.Text:password.Password;
   if(string.IsNullOrWhiteSpace(username.Text)||enteredPassword.Length==0){notice.Text="Enter your username and password.";return;}
   submit.IsEnabled=false;username.IsEnabled=false;password.IsEnabled=false;visiblePassword.IsEnabled=false;showPassword.IsEnabled=false;notice.Text="Checking account access...";
   try {
    await account.Login(username.Text.Trim(),enteredPassword,InstallationIdentity.Load(App.DataFolder),stop.Token);
    password.Clear();visiblePassword.Clear();await account.Validate(stop.Token);
    if(!stop.IsCancellationRequested)DialogResult=true;
   }catch(OperationCanceledException){if(!stop.IsCancellationRequested)notice.Text="Sign-in timed out. Try again, or email delta@acranix.com.";}
   catch(Exception ex){if(!stop.IsCancellationRequested)notice.Text=ex is System.Net.Http.HttpRequestException?"Cannot reach delta.acranix.com. Check your connection, or email delta@acranix.com.":ex.Message;}
   finally{enteredPassword="";password.Clear();visiblePassword.Clear();if(!stop.IsCancellationRequested){submit.IsEnabled=true;username.IsEnabled=true;password.IsEnabled=true;visiblePassword.IsEnabled=true;showPassword.IsEnabled=true;}}
  };
 }
#if DELTA_DIAGNOSTICS
 internal bool VerifyPasswordToggle() {
  password.Password="synthetic-ui-check";showPassword.IsChecked=true;
  bool revealed=visiblePassword.Visibility==Visibility.Visible&&visiblePassword.Text=="synthetic-ui-check";
  showPassword.IsChecked=false;
  bool hidden=password.Visibility==Visibility.Visible&&password.Password=="synthetic-ui-check"&&visiblePassword.Text.Length==0;
  password.Clear();return revealed&&hidden&&Opacity==.5;
 }
#endif
 void ToggleMaximize()=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
 static Grid Field(string label,UIElement input) {
  var grid=new Grid{Margin=new Thickness(0,0,0,16)};grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(112)});grid.ColumnDefinitions.Add(new ColumnDefinition());
  grid.Children.Add(new TextBlock{Text=label,FontSize=15,FontWeight=FontWeights.SemiBold,Foreground=ColorBrush("#D0DEEB"),VerticalAlignment=VerticalAlignment.Center});Grid.SetColumn(input,1);grid.Children.Add(input);return grid;
 }
 static Grid Watermark(TextBox input,string text) {
  var grid=new Grid();grid.Children.Add(input);var hint=new TextBlock{Text=text,Foreground=ColorBrush("#738EA9"),FontSize=15,Margin=new Thickness(13,0,0,0),VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};grid.Children.Add(hint);
  input.TextChanged+=(_,_)=>hint.Visibility=input.Text.Length==0?Visibility.Visible:Visibility.Collapsed;return grid;
 }
}

