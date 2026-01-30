using System.Windows;

namespace QuizHelper
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            // Velopack 초기화 (필수)
            // 앱이 설치/업데이트/삭제될 때 필요한 작업을 수행합니다.
            Velopack.VelopackApp.Build().Run();

            base.OnStartup(e);

            // 자동 업데이트 체크 시작
            await CheckForUpdates();
        }

        private async System.Threading.Tasks.Task CheckForUpdates()
        {
            try
            {
                // TODO: 실제 배포할 때 아래 URL을 본인의 웹사이트 주소로 변경하세요.
                // 예: "https://mysite.com/downloads/" 또는 GitHub Releases URL
                string updateUrl = "https://qplay-search.vercel.app/"; 

                var mgr = new Velopack.UpdateManager(updateUrl);

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
