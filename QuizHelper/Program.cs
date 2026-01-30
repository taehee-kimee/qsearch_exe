using System;

namespace QuizHelper
{
    public static class Program
    {
        [STAThread]
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
    }
}
