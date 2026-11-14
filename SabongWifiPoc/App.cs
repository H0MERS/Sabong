namespace SabongWifiPoc;
public sealed class App : Application { public App() => MainPage = new NavigationPage(new MainPage()); }
