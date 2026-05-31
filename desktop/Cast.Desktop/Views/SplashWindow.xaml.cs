using System.Windows;
using System.Windows.Input;
using XamlAnimatedGif;

namespace Cast.Desktop.Views;

public partial class SplashWindow : Window
{
    public event Action? Finished;
    private bool _isCompleted;

    /// <summary>Пользователь закрыл заставку крестиком (просит выйти из приложения)</summary>
    public bool UserClosed { get; private set; }

    public SplashWindow()
    {
        InitializeComponent();
    }

    private void OnAnimationLoaded(object sender, RoutedEventArgs e)
    {
        var animator = AnimationBehavior.GetAnimator(Player);

        if (animator != null)
        {
            animator.AnimationCompleted += (s, ev) => Complete();
        }
        else
        {
            Task.Delay(2000).ContinueWith(_ => Dispatcher.Invoke(Complete));
        }
    }

    private void Complete()
    {
        if (_isCompleted) return;
        _isCompleted = true;

        Finished?.Invoke();
        Close();
    }

    private void SplashScreenMove_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            this.DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        // Заставка живёт в отдельном STA-потоке. Нельзя звать Application.Shutdown()
        // отсюда — это упадёт (доступ к Application с чужого потока). Просто
        // помечаем намерение выйти и закрываем окно; завершение приложения берёт
        // на себя основной поток (App.OnStartup) по флагу UserClosed
        UserClosed = true;
        _isCompleted = true; // чтобы анимация по завершении не вызвала Finished
        Close();
    }
}