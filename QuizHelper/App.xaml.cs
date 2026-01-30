using System.Windows;

namespace QuizHelper
{
    public partial class App : Application
    {
        [System.STAThread]
        public static void Main(string[] args)
        {
            // Velopack 초기화 (필수) - 반드시 가장 먼저 호출!
            // 앱이 설치/업데이트/삭제될 때 필요한 작업을 수행합니다.
            // 이 메서드는 설치/업데이트/제거 작업이면 해당 작업을 수행하고 프로그램을 종료합니다.
            // 일반 실행이면 그냥 리턴됩니다.
            Velopack.VelopackApp.Build().Run();

            // WPF 애플리케이션 시작
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // MainWindow 생성 및 표시
            var mainWindow = new MainWindow();
            mainWindow.Show();

            // 자동 업데이트 체크 시작
            await CheckForUpdates();
        }

        private async System.Threading.Tasks.Task CheckForUpdates()
        {
            try
            {
                // GitHub Releases를 업데이트 소스로 사용
                // 첫 번째 인자: 리포지토리 URL
                // 두 번째 인자: 액세스 토큰 (공개 리포지토리라면 null)
                // 세 번째 인자: prerelease 포함 여부 (false = 정식 버전만)
                var source = new Velopack.Sources.GithubSource("https://github.com/taehee-kimee/qplay_search", null, false);
                var mgr = new Velopack.UpdateManager(source);

                // 새 버전 확인
                var newVersion = await mgr.CheckForUpdatesAsync();

                if (newVersion != null)
                {
                    // 새 버전이 있으면 다운로드
                    await mgr.DownloadUpdatesAsync(newVersion);

                    // 사용자에게 알림 (선택 사항 - 조용히 업데이트하려면 이 부분 제거 가능)
                    var result = MessageBox.Show(
                        $"새로운 버전({newVersion.TargetFullRelease.Version})이 다운로드 되었습니다.\n지금 재시작하여 적용하시겠습니까?",
                        "업데이트 알림",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        // 적용 후 재시작
                        mgr.ApplyUpdatesAndRestart(newVersion);
                    }
                }
            }
            catch (System.Exception ex)
            {
                // 업데이트 체크 실패 시 조용히 넘어감 (로그 남기기 가능)
                System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
            }
        }
    }
}
