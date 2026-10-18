using Avalonia.Controls;
using Kavosh.App.Ui;
using Kavosh.App.Views;
using Kavosh.Core;

namespace Kavosh.App.Stations
{
    public sealed class DisplayStation : Station { public DisplayStation(TestSuiteRunner r, MainWindow w) : base(r) { Content = Kv.Text("Display"); } }
    public sealed class AudioStation : Station { public AudioStation(TestSuiteRunner r) : base(r) { Content = Kv.Text("Audio"); } }
    public sealed class MicCameraStation : Station { public MicCameraStation(TestSuiteRunner r) : base(r) { Content = Kv.Text("Mic"); } }
    public sealed class InputStation : Station { public InputStation(TestSuiteRunner r, MainWindow w) : base(r) { Content = Kv.Text("Input"); } }
}

namespace Kavosh.App.Views
{
    public sealed class ResultView : UserControl { public ResultView(AppModel m, FinalReport r) { Content = Kv.Text("Results"); } }
    public sealed class StorageView : UserControl { public StorageView(AppModel m, MainWindow w) { Content = Kv.Text("Storage"); } }
    public sealed class WasherSetupView : UserControl { public WasherSetupView(AppModel m, MainWindow w) { Content = Kv.Text("Washer"); } }
}
