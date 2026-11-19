#if AI_EDITION
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using QuizHelper.Services;
using QuizHelper.Windows;

namespace QuizHelper
{
    /// <summary>
    /// 그깽족보 AI판: 족보에 없는 문제를 AI에게 물어 '🤖 AI 추정' 칸에 보여준다.
    /// 이 파일은 AI판 빌드(-p:Edition=AI)에만 들어간다. 일반판에는 코드가 아예 없다.
    /// 흐름·기준값은 웹 족보(js/app.js askAi / askAiVision)와 같다.
    /// </summary>
    public partial class MainWindow
    {
        private const int AiScanIntervalMs = 1000;  // 윈도우 화면 인식은 가벼워서 1초마다 본다 (문제를 더 빨리 알아챔)
        private const int AiSure = 80;              // OX: Jev 확신이 이보다 낮으면 Claude에게 한 번 더
        private const int AiSureChoice = 70;        // 보기 문제: Jev가 고른 보기의 확률 기준
        private const int AiVisionBelow = 30;       // Jev가 거의 찍은 수준이면 글자 대신 캡처 사진으로 확인
        private const double JevVisionSure = 0.8;   // Jev 사진 확신이 이보다 낮으면 Claude 사진으로
        private static readonly TimeSpan AiVisionInterval = TimeSpan.FromSeconds(10);

        private static readonly Brush AiPurple = new SolidColorBrush(Color.FromRgb(0xC9, 0xA7, 0xFF));
        private static readonly Brush AiYellow = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0x00));
        private static readonly Brush AiGreen = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x00));

        private TextBlock _aiToggle = null!;
        private Border _aiBorder = null!;
        private TextBlock _aiHeaderText = null!;
        private TextBlock _aiQuestionText = null!;
        private TextBlock _aiNoteText = null!;
        private StackPanel _aiRows = null!;

        private bool _aiScanBusy;
        private System.Drawing.Bitmap? _aiBitmap;   // 마지막 캡처 (사진 질문용)

        private string _aiContextNote = string.Empty; // "족보 답이 보기에 없어요" 같은, 이번 문제에만 붙는 안내
        private AiView? _aiView;      // 지금 칸에 떠 있는 내용 (진행 표시 포함)
        private AiView? _aiKept;      // 마지막으로 나온 답 (같은 문제를 다시 읽었을 때 다시 붙임)
        private string _aiKey = string.Empty;
        private string _aiLastQuestion = string.Empty;
        private string _aiLastChoices = string.Empty;
        private string _aiLastSheet = string.Empty;
        private DateTime _aiLastTime;
        private string _aiAnsweredQ = string.Empty;
        private DateTime _aiAnsweredTime;
        private DateTime _aiVisionTime;
        private string _aiVisionRaw = string.Empty;
        private string _aiNotQuizRaw = string.Empty;
        private DateTime _aiNotQuizTime;

        // ===== 화면 만들기 =====

        private void InitAi()
        {
            Title = "그깽족보 AI";
            _scanTimer.Interval = TimeSpan.FromMilliseconds(AiScanIntervalMs);
            AiAskService.LoadSettings();

            // 헤더의 "🤖 AI 꺼짐/켜짐"
            _aiToggle = new TextBlock
            {
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            _aiToggle.MouseLeftButtonDown += AiToggle_MouseLeftButtonDown;
            ((Panel)VersionBadge.Parent).Children.Add(_aiToggle);
            UpdateAiToggle();

            // 결과 영역의 보라색 "🤖 AI 추정" 칸
            _aiHeaderText = new TextBlock { Foreground = AiPurple, FontSize = 10, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center };
            _aiQuestionText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 4)
            };
            _aiNoteText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                FontSize = 10, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 2)
            };
            _aiRows = new StackPanel();
            var stack = new StackPanel();
            stack.Children.Add(_aiHeaderText);
            stack.Children.Add(_aiQuestionText);
            stack.Children.Add(_aiNoteText);
            stack.Children.Add(_aiRows);
            _aiBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0xB3, 0x88, 0xFF)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(0x22, 0xB3, 0x88, 0xFF)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8),
                Child = stack,
                Visibility = Visibility.Collapsed
            };
            ResultPanel.Children.Insert(ResultPanel.Children.IndexOf(ResultText), _aiBorder);
        }

        private void UpdateAiToggle()
        {
            _aiToggle.Text = AiAskService.On ? "🤖 AI 켜짐" : "🤖 AI 꺼짐";
            _aiToggle.Foreground = AiAskService.On ? AiPurple : new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF));
            _aiToggle.ToolTip = AiAskService.On
                ? "족보에 없는 문제를 AI가 추정해요. 클릭: 끄기"
                : "클릭: AI 켜기 (비밀번호 필요)";
        }

        private void AiToggle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true; // 창 드래그로 넘어가지 않게
            if (AiAskService.On)
            {
                AiAskService.SaveSettings(false, AiAskService.Pass);
                AiCancel();
            }
            else if (AiAskService.Pass.Length > 0)
            {
                AiAskService.SaveSettings(true, AiAskService.Pass);
            }
            else
            {
                var dialog = new AiPasswordWindow { Owner = this };
                if (dialog.ShowDialog() == true)
                    AiAskService.SaveSettings(true, dialog.Password);
            }
            UpdateAiToggle();
        }

        private void AiRender(AiView view, bool keep = true)
        {
            _aiView = view;
            if (keep) _aiKept = view;

            _aiHeaderText.Text = view.Header;
            _aiQuestionText.Text = TruncateText(view.Question, 150);
            _aiQuestionText.Visibility = string.IsNullOrWhiteSpace(view.Question) ? Visibility.Collapsed : Visibility.Visible;
            string note = string.Join(Environment.NewLine, new[] { _aiContextNote, view.Note }.Where(n => !string.IsNullOrWhiteSpace(n)));
            _aiNoteText.Text = note;
            _aiNoteText.Visibility = note.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

            _aiRows.Children.Clear();
            foreach (var row in view.Rows)
                _aiRows.Children.Add(AiRowBlock(row));
            if (!string.IsNullOrEmpty(view.Pending))
            {
                _aiRows.Children.Add(new TextBlock
                {
                    Text = view.Pending,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                    FontSize = 12, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
                });
            }

            ResultText.Visibility = Visibility.Collapsed;
            QuestionText.Visibility = Visibility.Collapsed;
            AnswerText.Visibility = Visibility.Collapsed;
            AlternativesBorder.Visibility = Visibility.Collapsed;
            _aiBorder.Visibility = Visibility.Visible;
        }

        private TextBlock AiRowBlock(AiRow row)
        {
            var block = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = "클릭하면 복사됩니다",
                Margin = new Thickness(0, 2, 0, 2),
                Opacity = row.Weak ? 0.6 : 1.0,
                Tag = row.Answer
            };
            block.Inlines.Add(new Run(row.Label + "  ") { Foreground = AiPurple, FontSize = 10 });
            block.Inlines.Add(new Run(row.Answer) { Foreground = AiYellow, FontSize = row.Weak ? 16 : 22, FontWeight = FontWeights.Bold });
            if (row.Weak)
                block.Inlines.Add(new Run("  (참고만)") { Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)), FontSize = 10 });
            block.MouseLeftButtonDown += (s, e) => e.Handled = true;
            block.MouseLeftButtonUp += (s, e) =>
            {
                string text = AiAskService.CleanCopyAnswer(row.Answer);
                if (text.Length == 0) return;
                try
                {
                    Clipboard.SetText(text);
                    StatusText.Text = $"📋 복사됨: {text}";
                    AiFlash(block);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CLIPBOARD] 복사 실패: {ex.Message}");
                }
            };
            return block;
        }

        /// <summary>복사됐다는 표시: 답 글씨를 잠깐 초록으로</summary>
        private void AiFlash(TextBlock block)
        {
            var answerRun = block.Inlines.OfType<Run>().Skip(1).FirstOrDefault();
            if (answerRun == null) return;
            answerRun.Foreground = AiGreen;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (s, e) =>
            {
                answerRun.Foreground = AiYellow;
                timer.Stop();
            };
            timer.Start();
        }

        /// <summary>칸만 숨긴다 (묻고 있는 질문은 계속 기다림)</summary>
        private void AiHide()
        {
            if (_aiBorder != null) _aiBorder.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 족보에서 답을 찾았거나(정답 화면 포함) AI를 껐을 때: 칸을 숨기고, 오는 중인 AI 답도 버린다.
        /// 화면이 넘어간 것이므로 '방금 답한 문제' 기억도 지운다 (OX처럼 빨리 넘어가는 다음 문제에 이전 답이 남지 않게).
        /// </summary>
        private void AiCancel()
        {
            _aiKey = string.Empty;
            _aiView = null;
            _aiAnsweredTime = default;
            AiHide();
        }

        private void AiReshow()
        {
            var view = _aiView ?? _aiKept;
            if (view != null) AiRender(view, keep: false);
        }

        /// <summary>스캔한 캡처를 사진 질문용으로 보관 (이전 것은 정리)</summary>
        private void AiKeepCapture(System.Drawing.Bitmap bitmap)
        {
            _aiBitmap?.Dispose();
            _aiBitmap = bitmap;
        }

        // ===== 같은 문제 판단 =====

        private void AiMarkAnswered(string question)
        {
            _aiAnsweredQ = question ?? string.Empty;
            _aiAnsweredTime = DateTime.Now;
        }

        /// <summary>방금 답한 문제가 조금 다르게(또는 깨져) 읽힌 것인지 → 다시 묻지 않는다</summary>
        private bool AiRecentAnswerFor(string question)
        {
            if (_aiKept == null || _aiAnsweredTime == default) return false;
            double age = (DateTime.Now - _aiAnsweredTime).TotalMilliseconds;
            // 답이 나온 지 8초도 안 됐으면 같은 문제다 (문제는 보통 20초 넘게 걸림)
            if (age < 8000) return true;
            if (!string.IsNullOrEmpty(question) && _aiAnsweredQ.Length > 0)
                return age < 20000 && AiAskService.Similarity(question, _aiAnsweredQ) >= 60;
            return age < 12000; // 문제 글을 아예 못 읽었으면 방금 답한 문제로 본다
        }

        private static string AiRawHead(string ocrText)
        {
            string raw = Regex.Replace(ocrText ?? string.Empty, @"\s+", " ").Trim();
            return raw.Length > 200 ? raw.Substring(0, 200) : raw;
        }

        // ===== 족보에 없을 때 =====

        /// <summary>족보에서 못 찾은 화면. 문제 화면이면 AI에게 묻는다.</summary>
        private void AiOnNoMatch(string ocrText)
        {
            if (!AiAskService.On) return;
            string? category = _csvDataService.CurrentCategory;
            if (category == null || !CollectSync.SheetNames.TryGetValue(category, out var sheet)) return;

            var q = AiAskService.BuildQuestion(ocrText, sheet, _aiBitmap != null);
            if (q == null)
            {
                // 문제 화면이 아님 → 평소처럼 "매칭되는 답을 찾지 못했습니다".
                // 정답 발표 화면이면 문제가 끝난 것이므로 AI 답 기억을 지운다 (곧바로 나오는 다음 문제에 이전 답이 남지 않게)
                if (Regex.IsMatch(ocrText, @"정\s*[답담]\s*[은는]")) AiCancel();
                return;
            }

            if (q.Vision) _ = AiAskVisionAsync(q, ocrText, sheet);
            else _ = AiAskTextAsync(q, ocrText, sheet);
        }

        /// <summary>
        /// 족보가 답을 냈지만 그 답이 화면 보기 중에 없으면, 질문만 비슷한 다른 문제다
        /// ("다음 중 …아닌 것은?"처럼 질문은 같고 보기만 다른 문제가 많다) → AI에게 묻는다.
        /// AI에게 물었으면 true.
        /// </summary>
        private bool AiOnJokboMatch(string ocrText, Models.MatchResult bestMatch)
        {
            if (!AiAskService.On) return false;
            string? category = _csvDataService.CurrentCategory;
            if (category == null || !CollectSync.SheetNames.TryGetValue(category, out var sheet)) return false;
            if (CsvDataService.ClassifyScreen(ocrText).Type != ScreenType.Question) return false;

            var q = AiAskService.ParseRaw(ocrText);
            // 보기 4개가 깨끗하게 읽혔을 때만 판단한다 (보기가 덜 읽혔으면 족보 답이 안 읽힌 보기일 수 있음)
            if (q == null || q.Choices.Count < 4 || !AiAskService.QuestionClean(q)) return false;

            bool InChoices(string answer) => q.Choices.Values.Any(c => QuestionCollector.AnswersEquivalent(answer, c, 80, 2));
            if (InChoices(bestMatch.Answer)) return false;

            string jokboAnswer = AiAskService.CleanCopyAnswer(bestMatch.Answer);
            string note = $"족보의 비슷한 문제 답 '{jokboAnswer}'은(는) 보기에 없어요.";
            // 2·3등 후보 중 보기에 있는 답이 있으면 함께 알려 준다 (AI 답과 같으면 믿을 만함)
            var top = _csvDataService.FindTopMatches(ocrText, FuzzyMatchThreshold, 3);
            var alt = top.Candidates.FirstOrDefault(c => InChoices(c.Answer));
            if (alt != null) note += $" 족보 후보: {AiAskService.CleanCopyAnswer(alt.Answer)}";
            _ = AiAskTextAsync(q, ocrText, sheet, note);
            return true;
        }

        private bool AiStillCurrent(string key, string sheet) =>
            key == _aiKey && _isScanning && AiAskService.On &&
            _csvDataService.CurrentCategory != null &&
            CollectSync.SheetNames.TryGetValue(_csvDataService.CurrentCategory, out var now) && now == sheet;

        private static string AiJevLabel(int? conf) => conf.HasValue ? $"Jev {conf}%" : "Jev";

        private static string AiVisionLabel(AiReply reply) =>
            reply.Model == "jev-vision"
                ? (reply.Confidence.HasValue ? $"📷 Jev {Math.Round(reply.Confidence.Value * 100)}%" : "📷 Jev")
                : "📷 Claude";

        private void AiHandleBadPassword()
        {
            AiAskService.SaveSettings(false, string.Empty);
            UpdateAiToggle();
            AiCancel();
            ResultText.Text = "AI 비밀번호가 바뀌었어요.\n🤖 AI 버튼을 눌러 다시 켜 주세요.";
            ResultText.Visibility = Visibility.Visible;
        }

        private async Task AiAskTextAsync(AiQuestion q, string ocrText, string sheet, string contextNote = "")
        {
            try
            {
                string choicesJoined = string.Join(" ", q.Choices.Values);
                string key = sheet + "|" + q.Question + "|" + string.Join("/", q.Choices.Select(kv => kv.Key + "." + kv.Value));
                var now = DateTime.Now;

                // 같은 문제를 계속 다시 읽으면 글자가 조금씩 달라진다 → 비슷하면 다시 묻지 않는다
                // 보기가 있으면 보기가 같고 질문이 75점 이상, 보기 없는 OX는 한 단어만 다른 다음 문제가 많아 92점 이상
                bool sameChoices = (choicesJoined.Length == 0 && _aiLastChoices.Length == 0)
                    || (choicesJoined.Length > 0 && _aiLastChoices.Length > 0 && AiAskService.Similarity(choicesJoined, _aiLastChoices) >= 85);
                bool sameAsLast = _aiLastQuestion.Length > 0 && _aiLastSheet == sheet
                    && (now - _aiLastTime).TotalMilliseconds < 15000
                    && AiAskService.Similarity(q.Question, _aiLastQuestion) >= (q.Choices.Count > 0 ? 75 : 92)
                    && sameChoices;
                if (key == _aiKey || sameAsLast || AiRecentAnswerFor(q.Question))
                {
                    // 답이 있거나 묻는 중이면 그대로 둔다. (묻는 도중 화면이 잠깐 다르게 읽혀 취소됐던 문제면 아래에서 다시 묻는다)
                    if (_aiView != null || _aiKept != null || _aiKey.Length > 0)
                    {
                        AiReshow();
                        return;
                    }
                }

                _aiKey = key;
                _aiContextNote = contextNote;
                _aiLastQuestion = q.Question;
                _aiLastTime = now;
                _aiLastSheet = sheet;
                _aiLastChoices = choicesJoined;

                // 전에 확신 있게 답 받은 문제면 AI를 다시 부르지 않는다
                var cached = AiAskService.CacheGet(sheet, q);
                if (cached != null)
                {
                    AiRender(cached);
                    AiMarkAnswered(q.Question);
                    AiAutoCopyIfKkong(cached.CopyAnswer);
                    return;
                }

                _aiKept = null;
                AiRender(new AiView { Pending = "생각하는 중…" }, keep: false);

                var reply = await AiAskService.AskTextAsync(sheet, q);
                if (!AiStillCurrent(key, sheet)) return;
                if (!reply.Success)
                {
                    if (reply.Error == "bad-password") { AiHandleBadPassword(); return; }
                    AiRender(new AiView
                    {
                        Pending = reply.Error == "rate-limited" ? "잠깐 너무 많이 물어봤어요. 1분 뒤 다시 시도해 주세요."
                            : reply.Error.StartsWith("network") ? "AI 서버에 연결하지 못했어요."
                            : "AI가 답하지 못했어요."
                    });
                    return;
                }

                // Jev 확신: 보기 문제는 고른 보기의 확률(70%+), OX는 confidence(80%+)
                bool isChoiceQ = sheet != AiAskService.SheetOx && q.Choices.Count >= 2;
                bool byProbability = isChoiceQ && reply.Probability.HasValue;
                int? conf = byProbability ? (int)Math.Round(reply.Probability!.Value * 100)
                    : reply.Confidence.HasValue ? (int)Math.Round(reply.Confidence.Value * 100) : null;
                int sureLine = byProbability ? AiSureChoice : AiSure;
                bool unsure = reply.Model == "jev" && conf.HasValue && conf.Value < sureLine;
                string firstLabel = reply.Model == "jev" ? AiJevLabel(conf) : "Claude";

                if (!unsure)
                {
                    var view = new AiView { Question = q.Question, CopyAnswer = reply.Answer };
                    view.Rows.Add(new AiRow { Label = firstLabel, Answer = reply.Answer });
                    AiRender(view);
                    AiMarkAnswered(q.Question);
                    AiAskService.CachePut(sheet, q, view);
                    AiAutoCopyIfKkong(reply.Answer);
                    return;
                }

                // Jev가 확신하지 못함 → Jev 답을 먼저 흐리게 보여주고 두 번째 의견을 받는다
                var firstRow = new AiRow { Label = firstLabel, Answer = reply.Answer, Weak = true };
                AiRender(new AiView { Question = q.Question, Rows = { firstRow }, Pending = "Claude 확인하는 중…" }, keep: false);

                // Jev가 거의 찍은 수준(30% 미만)이면 글자가 잘못 읽혔을 수 있어 캡처 사진으로 확인하고, 그 외엔 Claude 글자로
                int? rawConf = reply.Confidence.HasValue ? (int)Math.Round(reply.Confidence.Value * 100) : null;
                AiReply? second = null;
                if (_aiBitmap != null && rawConf.HasValue && rawConf.Value < AiVisionBelow)
                {
                    second = await AiAskVisionSmartAsync(q, sheet, partial =>
                    {
                        if (!AiStillCurrent(key, sheet)) return;
                        AiRender(new AiView
                        {
                            Question = q.Question,
                            Rows = { firstRow, new AiRow { Label = AiVisionLabel(partial), Answer = partial.Answer, Weak = true } },
                            Pending = "📷 Claude 확인하는 중…"
                        }, keep: false);
                    }, skipJev: true);
                    _aiVisionTime = DateTime.Now; // 같은 화면에 사진을 또 보내지 않게
                }
                if (second == null || !second.Success)
                    second = await AiAskService.AskTextAsync(sheet, q, second: true);
                if (!AiStillCurrent(key, sheet)) return;

                if (!second.Success)
                {
                    AiRender(new AiView { Question = q.Question, Rows = { firstRow } });
                    return;
                }

                AiMarkAnswered(q.Question);
                bool agree = second.Choice.Length > 0 && second.Choice == reply.Choice;
                bool byVision = second.Model == "claude-vision" || second.Model == "jev-vision";
                if (agree)
                {
                    // Jev가 자신 없는 문제에서 둘이 같은 답이면 89% 정답
                    var view = new AiView { Question = q.Question, CopyAnswer = reply.Answer };
                    view.Rows.Add(new AiRow { Label = byVision ? "Jev·사진 일치" : "Jev·Claude 일치", Answer = reply.Answer });
                    AiRender(view);
                    AiAskService.CachePut(sheet, q, view);
                    AiAutoCopyIfKkong(reply.Answer);
                }
                else if (byVision)
                {
                    // 사진을 본 쪽이 화면을 제대로 읽었을 가능성이 높다 → 사진 답을 위에 진하게
                    AiRender(new AiView
                    {
                        Question = q.Question,
                        Note = second.Question.Length > 0 ? "📷 사진으로 다시 읽은 문제: " + second.Question : string.Empty,
                        Rows = { new AiRow { Label = AiVisionLabel(second), Answer = second.Answer }, firstRow },
                        CopyAnswer = second.Answer
                    });
                    AiAutoCopyIfKkong(second.Answer);
                }
                else
                {
                    // 갈리면 더 높은 모델(Claude) 답을 위에. 실측상 반반이라 둘 다 흐리게
                    AiRender(new AiView
                    {
                        Question = q.Question,
                        Note = "⚠️ AI 의견이 갈려요. 둘 다 참고만 하세요.",
                        Rows = { new AiRow { Label = "Claude", Answer = second.Answer, Weak = true }, firstRow }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AI] 글자 질문 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 캡처 사진으로 묻기. OX·올라올라는 Jev가 먼저 보고(빠름), 자신 없거나 실패하면 Claude가 본다.
        /// </summary>
        private async Task<AiReply?> AiAskVisionSmartAsync(AiQuestion? q, string sheet, Action<AiReply>? onPartial, bool skipJev = false)
        {
            var bitmap = _aiBitmap;
            if (bitmap == null) return null;
            // 기다리는 동안 다음 캡처로 바뀌므로 지금 화면을 먼저 사진으로 만들어 둔다
            byte[]? plain = AiAskService.EncodeJpeg(bitmap, 0);
            if (plain == null) return null;

            if (!skipJev && (sheet == AiAskService.SheetOx || sheet == AiAskService.SheetOlla))
            {
                byte[]? padded = bitmap.Width >= 280 && bitmap.Height >= 280 && bitmap.Width <= 1024
                    ? plain : AiAskService.EncodeJpeg(bitmap, 280);
                AiReply? jev = padded != null ? await AiAskService.AskImageAsync(sheet, padded, jev: true) : null;
                if (jev != null)
                {
                    // Jev가 '퀴즈 화면이 아님'이라고 했으면 Claude 사진도 부르지 않는다
                    if (jev.Error == "not-quiz" || jev.Error == "bad-password") return jev;
                    if (jev.Success)
                    {
                        string n = jev.Choice;
                        string? choiceText = q != null && q.Choices.TryGetValue(n, out var c) ? c : null;
                        jev.Answer = sheet == AiAskService.SheetOx ? n
                            : (choiceText != null && !AiAskService.LooksGarbled(choiceText, true) ? $"{n}. {choiceText}" : $"{n}번");
                        // 퀴즈 화면인지 애매하면(70% 미만) 답 확신이 높아도 Claude 사진으로 확인한다
                        bool screenOk = !jev.Screen.HasValue || jev.Screen.Value >= 0.7;
                        if (screenOk && jev.Confidence.HasValue && jev.Confidence.Value >= JevVisionSure) return jev;
                        onPartial?.Invoke(jev);
                    }
                }
            }
            return await AiAskService.AskImageAsync(sheet, plain, jev: false);
        }

        private async Task AiAskVisionAsync(AiQuestion q, string ocrText, string sheet)
        {
            try
            {
                if (_aiBitmap == null) return;
                if (AiRecentAnswerFor(q.Question))
                {
                    AiReshow(); // 이미 답이 떠 있는 문제가 잠깐 깨져 읽힌 것 → 사진을 다시 보내지 않는다
                    return;
                }

                var now = DateTime.Now;
                string raw = AiRawHead(ocrText);
                // 같은 화면(원본 글자가 비슷함)이면 30초 동안 다시 보내지 않는다
                bool sameScreen = _aiVisionRaw.Length > 0 && (now - _aiVisionTime).TotalMilliseconds < 30000
                    && AiAskService.Similarity(raw, _aiVisionRaw) >= 70;
                // 방금 '퀴즈 아님'이던 화면이면 1분간 다시 안 보낸다 (글자 길이도 비슷해야 같은 화면)
                bool sameLength = _aiNotQuizRaw.Length > 0
                    && Math.Abs(raw.Length - _aiNotQuizRaw.Length) <= Math.Max(raw.Length, _aiNotQuizRaw.Length) * 0.3;
                if (sameLength && (now - _aiNotQuizTime).TotalMilliseconds < 60000 && AiAskService.Similarity(raw, _aiNotQuizRaw) >= 70)
                    return;
                if (now - _aiVisionTime < AiVisionInterval || sameScreen)
                {
                    AiReshow();
                    return;
                }

                _aiVisionTime = now;
                _aiVisionRaw = raw;
                string key = "vision|" + now.Ticks;
                _aiKey = key;
                _aiContextNote = string.Empty;
                _aiKept = null;
                AiRender(new AiView { Pending = "📷 글자가 흐려서 화면 사진으로 확인하는 중…" }, keep: false);

                var reply = await AiAskVisionSmartAsync(q, sheet, partial =>
                {
                    if (!AiStillCurrent(key, sheet)) return;
                    AiRender(new AiView
                    {
                        Header = "🤖 AI 추정 · 📷 사진으로 읽음",
                        Question = q.Question,
                        Rows = { new AiRow { Label = AiVisionLabel(partial), Answer = partial.Answer, Weak = true } },
                        Pending = "📷 Claude 확인하는 중…"
                    }, keep: false);
                });
                if (reply == null || !AiStillCurrent(key, sheet)) return;
                if (reply.Error == "bad-password") { AiHandleBadPassword(); return; }
                if (reply.Error == "not-quiz")
                {
                    // 퀴즈 화면이 아니었음(대기실·순위·문제 넘어가는 중) → 칸을 지우고 '이 화면'만 1분간 다시 안 보낸다
                    AiCancel();
                    ResultText.Visibility = Visibility.Visible;
                    _aiNotQuizRaw = raw;
                    _aiNotQuizTime = DateTime.Now;
                    _aiVisionTime = default;
                    _aiVisionRaw = string.Empty;
                    return;
                }
                if (!reply.Success)
                {
                    AiRender(new AiView { Pending = reply.Error.StartsWith("network") ? "AI 서버에 연결하지 못했어요." : "AI가 답하지 못했어요." });
                    return;
                }

                string question = reply.Question.Length > 0 ? reply.Question : q.Question;
                // 같은 문제를 이어서 글자로 다시 묻지 않도록
                _aiLastQuestion = question;
                _aiLastSheet = sheet;
                _aiLastTime = DateTime.Now;
                _aiLastChoices = string.Join(" ", reply.Choices);

                var view = new AiView { Header = "🤖 AI 추정 · 📷 사진으로 읽음", Question = question, CopyAnswer = reply.Answer };
                view.Rows.Add(new AiRow { Label = AiVisionLabel(reply), Answer = reply.Answer });
                AiRender(view);
                AiMarkAnswered(question);
                if (reply.Question.Length > 0)
                {
                    var readQ = new AiQuestion { Question = reply.Question };
                    for (int i = 0; i < reply.Choices.Count; i++) readQ.Choices[i.ToString()] = reply.Choices[i];
                    AiAskService.CachePut(sheet, readQ, view);
                }
                AiAutoCopyIfKkong(reply.Answer);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AI] 사진 질문 오류: {ex.Message}");
            }
        }

        /// <summary>꽁꽁: 족보에 없는 문제의 AI 답(한글)을 자동 복사</summary>
        private void AiAutoCopyIfKkong(string answer)
        {
            if (_csvDataService.CurrentCategory?.Equals("kkong", StringComparison.OrdinalIgnoreCase) != true) return;
            string text = AiAskService.CleanCopyAnswer(answer);
            bool hangul = Regex.IsMatch(text, "[가-힣]");
            if (text.Length == 0 || !Regex.IsMatch(text, "[가-힣0-9]") || (hangul && Regex.IsMatch(text, "[a-zA-Z]")))
                return; // 한글 답이 아니면 복사하지 않는다
            try
            {
                Clipboard.SetText(text);
                StatusText.Text = $"📋 복사됨: {text}";
                if (_aiRows.Children.Count > 0 && _aiRows.Children[0] is TextBlock first) AiFlash(first);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CLIPBOARD] 복사 실패: {ex.Message}");
            }
        }
    }
}

namespace QuizHelper.Windows
{
    /// <summary>AI 비밀번호 입력 창 (웹 족보와 같은 비밀번호, 서버가 확인)</summary>
    internal class AiPasswordWindow : Window
    {
        private readonly PasswordBox _box;
        private readonly TextBlock _message;
        private readonly Button _okButton;

        public string Password { get; private set; } = string.Empty;

        public AiPasswordWindow()
        {
            Title = "AI 비밀번호";
            Width = 300;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Topmost = true;

            var title = new TextBlock { Text = "🤖 AI 켜기", Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold };
            var guide = new TextBlock
            {
                Text = "비밀번호를 입력하세요.",
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)), FontSize = 12, Margin = new Thickness(0, 8, 0, 8)
            };
            _box = new PasswordBox
            {
                FontSize = 14, Padding = new Thickness(6, 4, 6, 4),
                Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)), Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), CaretBrush = Brushes.White
            };
            _box.KeyDown += (s, e) => { if (e.Key == Key.Enter) _ = SubmitAsync(); };
            _message = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80)), FontSize = 11,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), MinHeight = 16
            };

            var style = (Style)Application.Current.FindResource("TransparentButton");
            var cancel = new Button { Content = "취소", Style = style, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            cancel.Click += (s, e) => { DialogResult = false; };
            _okButton = new Button { Content = "확인", Style = style };
            _okButton.Click += (s, e) => _ = SubmitAsync();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            buttons.Children.Add(cancel);
            buttons.Children.Add(_okButton);

            var stack = new StackPanel { Margin = new Thickness(16) };
            stack.Children.Add(title);
            stack.Children.Add(guide);
            stack.Children.Add(_box);
            stack.Children.Add(_message);
            stack.Children.Add(buttons);

            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Child = stack
            };
            MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
            Loaded += (s, e) => _box.Focus();
        }

        private async Task SubmitAsync()
        {
            string pass = _box.Password.Trim();
            if (pass.Length == 0 || !_okButton.IsEnabled) return;
            _okButton.IsEnabled = false;
            _message.Text = "확인하는 중…";
            string error = await AiAskService.VerifyAsync(pass);
            if (error.Length == 0)
            {
                Password = pass;
                DialogResult = true;
                return;
            }
            _message.Text = error == "bad-password" ? "비밀번호가 달라요."
                : error == "rate-limited" ? "너무 여러 번 시도했어요. 1분 뒤 다시 해 주세요."
                : "서버에 연결하지 못했어요. 인터넷을 확인해 주세요.";
            _okButton.IsEnabled = true;
            _box.SelectAll();
            _box.Focus();
        }
    }
}
#endif
