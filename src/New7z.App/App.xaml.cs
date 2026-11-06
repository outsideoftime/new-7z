using Microsoft.UI.Xaml;
namespace New7z.App;
public partial class App:Application
{
    public static MainWindow? Window { get; private set; }
    private Microsoft.UI.Xaml.Window? taskWindow;
    public App(){InitializeComponent();UnhandledException+=(_,e)=>{try{var folder=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"New7z");Directory.CreateDirectory(folder);System.IO.File.AppendAllText(System.IO.Path.Combine(folder,"error.log"),DateTimeOffset.Now.ToString("o")+" "+e.Exception+Environment.NewLine);}catch{}};}
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var command=Environment.GetCommandLineArgs().Skip(1).ToArray();
        if(command.Length==2&&command[0]=="--shell-request")
        {
            try
            {
                var request=await ShellRequest.ReadAsync(command[1]);
                if(request.Action=="open"){Window=new MainWindow(request.Paths[0]);Window.Activate();}
                else {taskWindow=new ShellTaskWindow(request);taskWindow.Activate();}
            }
            catch(Exception ex){taskWindow=new ShellTaskWindow(null,ex.Message);taskWindow.Activate();}
            return;
        }
        Window=new MainWindow();Window.Activate();
    }
}
