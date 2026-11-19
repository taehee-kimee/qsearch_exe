using System.Windows;

namespace QuizHelper
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

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
#if AI_EDITION
                // AI판은 일반판과 다른 저장소에서 업데이트를 받는다 (일반판 사용자에게는 보이지 않음).
                // 그 저장소에 다른 앱(일반판) 패키지가 섞여 있어도 AI판 패키지만 본다 — 안 거르면 버전 숫자가 더 큰
                // 일반판 패키지를 '새 버전'으로 알고 AI판이 일반판으로 바뀌어 버린다 (2026-11-20 실제로 확인).
                var source = new AiOnlySource(new Velopack.Sources.GithubSource("https://github.com/taehee-kimee/qsearch_exe", null, false));
#else
                var source = new Velopack.Sources.GithubSource("https://github.com/taehee-kimee/qplay_search", null, false);
#endif
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

#if AI_EDITION
        /// <summary>업데이트 목록에서 AI판(패키지 ID QuizHelperAI) 패키지만 남기는 소스</summary>
        private sealed class AiOnlySource : Velopack.Sources.IUpdateSource
        {
            private const string PackageId = "QuizHelperAI";
            private readonly Velopack.Sources.IUpdateSource _inner;

            public AiOnlySource(Velopack.Sources.IUpdateSource inner) { _inner = inner; }

            public async System.Threading.Tasks.Task<Velopack.VelopackAssetFeed> GetReleaseFeed(
                Microsoft.Extensions.Logging.ILogger logger, string channel, System.Guid? stagingId = null, Velopack.VelopackAsset? latestLocalRelease = null)
            {
                var feed = await _inner.GetReleaseFeed(logger, channel, stagingId, latestLocalRelease);
                var mine = new System.Collections.Generic.List<Velopack.VelopackAsset>();
                foreach (var asset in feed.Assets)
                {
                    if (string.Equals(asset.PackageId, PackageId, System.StringComparison.OrdinalIgnoreCase)) mine.Add(asset);
                }
                return new Velopack.VelopackAssetFeed { Assets = mine.ToArray() };
            }

            public System.Threading.Tasks.Task DownloadReleaseEntry(
                Microsoft.Extensions.Logging.ILogger logger, Velopack.VelopackAsset releaseEntry, string localFile,
                System.Action<int> progress, System.Threading.CancellationToken cancelToken = default)
                => _inner.DownloadReleaseEntry(logger, releaseEntry, localFile, progress, cancelToken);
        }
#endif
    }
}
