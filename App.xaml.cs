namespace ET_Ducky_Desktop_Public
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new MainPage()) { Title = "ET_Ducky-Desktop-Public" };
        }
    }
}
