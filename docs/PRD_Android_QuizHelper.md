# PRD: Android QuizHelper (모바일 퀴즈 도우미)

## 1. 개요

### 1.1 프로젝트 배경
현재 Windows용 QuizHelper는 게임 화면에서 퀴즈 문제를 OCR로 인식하고, 데이터베이스에서 정답을 찾아 표시하는 도구입니다. 이를 Android 모바일 환경으로 확장하여 모바일 게임에서도 동일한 기능을 제공합니다.

### 1.2 목표
- 모바일 게임 화면에서 OCR 영역 지정 및 텍스트 인식
- 퀴즈 데이터베이스에서 정답 자동 검색
- 오버레이 UI로 게임 플레이 중 실시간 정답 표시
- (추가 기능) 정답 자동 입력

### 1.3 대상 플랫폼
- **Android 9.0 (API 28) 이상**
- 접근성 서비스 사용으로 인해 iOS는 지원 불가

---

## 2. 핵심 기능 요구사항

### 2.1 기능 매핑 (PC → Android)

| PC 기능 | Android 구현 | 구현 난이도 |
|---------|-------------|------------|
| Windows OCR | Google ML Kit OCR | 쉬움 |
| 이미지 전처리 (2배 확대, 고대비) | Android Bitmap 처리 | 쉬움 |
| 창 선택 + 영역 지정 | 오버레이 드래그 영역 선택 | 중간 |
| 화면 캡처 (창 기반) | 접근성 서비스 takeScreenshot | 중간 |
| 2초 간격 자동 스캔 | Handler/Coroutine 반복 | 쉬움 |
| CSV 데이터 로드 | Room DB + CSV import | 쉬움 |
| 퍼지 매칭 (FuzzySharp) | FuzzyWuzzy-Kotlin/Java | 중간 |
| 역색인 검색 | Room FTS 또는 메모리 인덱스 | 중간 |
| 결과 오버레이 표시 | Floating Window (TYPE_APPLICATION_OVERLAY) | 중간 |
| 클립보드 복사 | ClipboardManager | 쉬움 |
| 자동 입력 (신규) | 접근성 서비스 performAction | 어려움 |

### 2.2 상세 기능 명세

#### 2.2.1 OCR 엔진
```
- 엔진: Google ML Kit Text Recognition v2
- 지원 언어: 한국어(ko), 영어(en)
- 오프라인 모드: 지원 (언어 팩 다운로드)
- 예상 성능:
  - 인식 시간: 50-200ms
  - 정확도: 90%+ (현재 Windows OCR과 동등)
```

#### 2.2.2 이미지 전처리
```kotlin
// PC 버전과 동일한 전처리 적용
fun preprocessImage(bitmap: Bitmap): Bitmap {
    // 1. 2배 확대 (Bicubic 보간)
    val scaled = Bitmap.createScaledBitmap(
        bitmap,
        bitmap.width * 2,
        bitmap.height * 2,
        true  // filter = true for better quality
    )

    // 2. 그레이스케일 + 대비 증가
    val colorMatrix = ColorMatrix().apply {
        setSaturation(0f)  // 그레이스케일
        val contrast = 1.5f
        val translate = (-0.5f * contrast + 0.5f) * 255f
        postConcat(ColorMatrix(floatArrayOf(
            contrast, 0f, 0f, 0f, translate,
            0f, contrast, 0f, 0f, translate,
            0f, 0f, contrast, 0f, translate,
            0f, 0f, 0f, 1f, 0f
        )))
    }

    // Canvas에 필터 적용 후 반환
    ...
}
```

#### 2.2.3 화면 캡처 시스템
```
방식: 접근성 서비스 (AccessibilityService)
API: takeScreenshot() - Android 9+

장점:
- MediaProjection 대비 사용자 허용 팝업 없음
- 백그라운드에서 지속 캡처 가능
- 다른 앱 위에 오버레이 가능

단점:
- 접근성 설정에서 수동 활성화 필요
- 일부 보안 앱/게임에서 차단 가능
```

#### 2.2.4 영역 선택 UI
```
1. 전체 화면 오버레이 표시 (반투명)
2. 사용자가 드래그하여 사각형 영역 선택
3. 선택 영역 좌표 저장 (화면 비율 기준)
4. 선택 완료 시 테두리(BorderView) 표시

저장 형식:
{
  "left_ratio": 0.1,    // 화면 너비의 10%
  "top_ratio": 0.3,     // 화면 높이의 30%
  "width_ratio": 0.8,   // 화면 너비의 80%
  "height_ratio": 0.2   // 화면 높이의 20%
}

→ 화면 회전/해상도 변경에도 동일 비율 유지
```

#### 2.2.5 퀴즈 데이터 관리
```
저장소: Room Database + FTS4 (Full-Text Search)

테이블 구조:
┌─────────────────────────────────────────────┐
│ quiz_entries                                 │
├─────────────────────────────────────────────┤
│ id: Long (PK, auto)                          │
│ question: String                             │
│ answer: String                               │
│ category: String                             │
│ keywords: String (검색용 토큰화된 키워드)      │
├─────────────────────────────────────────────┤
│ INDEX: idx_category ON (category)            │
│ FTS: quiz_fts ON (question, answer, keywords)│
└─────────────────────────────────────────────┘

데이터 동기화:
- 옵션 1: 앱 내장 CSV → 첫 실행 시 Room으로 import
- 옵션 2: 서버 API → 주기적 동기화 (향후 확장)
- 옵션 3: 사용자 CSV 업로드 기능
```

#### 2.2.6 매칭 알고리즘
```kotlin
// PC 버전과 동일한 3단계 매칭

class QuizMatcher(private val db: QuizDatabase) {

    // 1단계: 역색인 기반 후보 추출 (상위 100개)
    fun getCandidates(ocrText: String): List<QuizEntry> {
        val keywords = extractKeywords(ocrText)
        return db.quizDao().searchByKeywords(keywords, limit = 100)
    }

    // 2단계: 퍼지 매칭 (FuzzyWuzzy 사용)
    fun findBestMatch(ocrText: String): MatchResult? {
        val candidates = getCandidates(ocrText)
        val normalized = normalizeText(ocrText)

        return candidates
            .map { entry ->
                val score = maxOf(
                    FuzzySearch.partialRatio(normalized, normalizeText(entry.question)),
                    FuzzySearch.tokenSetRatio(normalized, normalizeText(entry.question)),
                    FuzzySearch.weightedRatio(normalized, normalizeText(entry.question))
                )
                MatchResult(entry, score)
            }
            .filter { it.score >= 80 }
            .maxByOrNull { it.score }
    }

    // 3단계: 화면 분류 (시스템 메시지, 정답 화면 필터링)
    fun classifyScreen(ocrText: String): ScreenType { ... }
}
```

#### 2.2.7 결과 표시 UI (Floating Overlay)
```
┌─────────────────────────────────────┐
│  [최소화] [설정] [X]                 │  ← 헤더 (드래그 이동 가능)
├─────────────────────────────────────┤
│  카테고리: kkong           ▼        │  ← 카테고리 선택
├─────────────────────────────────────┤
│                                     │
│  Q: 대한민국의 수도는?               │  ← 인식된 질문
│                                     │
│  ✓ 서울                             │  ← 정답 (탭하면 복사/입력)
│                                     │
│  ┌─────────────────────────────┐   │
│  │ 2. 부산 (78%)               │   │  ← 대안 답변
│  │ 3. 인천 (72%)               │   │
│  └─────────────────────────────┘   │
│                                     │
│  [▶ 시작] [■ 정지]     🔋 스캔 중...│  ← 컨트롤
└─────────────────────────────────────┘

최소화 모드:
┌─────────────────────────────────────┐
│  ✓ 서울                    [확대]   │
└─────────────────────────────────────┘
```

#### 2.2.8 자동 입력 (선택 기능)
```kotlin
// 접근성 서비스로 텍스트 입력
fun autoInputAnswer(answer: String) {
    // 1. 현재 포커스된 입력 필드 찾기
    val focusedNode = rootInActiveWindow?.findFocus(AccessibilityNodeInfo.FOCUS_INPUT)

    // 2. 텍스트 입력
    if (focusedNode?.isEditable == true) {
        val arguments = Bundle().apply {
            putCharSequence(AccessibilityNodeInfo.ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE, answer)
        }
        focusedNode.performAction(AccessibilityNodeInfo.ACTION_SET_TEXT, arguments)
    }
}

주의사항:
- 게임마다 입력 방식이 달라 호환성 이슈 가능
- 사용자 설정에서 on/off 토글 제공
```

---

## 3. 시스템 아키텍처

### 3.1 전체 구조
```
┌─────────────────────────────────────────────────────────────────┐
│                        Android App                               │
├─────────────────────────────────────────────────────────────────┤
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────────┐  │
│  │ MainActivity│  │SettingsAct.│  │  FloatingOverlayService │  │
│  │ (설정/관리) │  │ (환경설정)  │  │  (결과 표시 UI)         │  │
│  └──────┬──────┘  └─────────────┘  └───────────┬─────────────┘  │
│         │                                       │                │
│  ┌──────┴───────────────────────────────────────┴──────────────┐│
│  │                    QuizAccessibilityService                 ││
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────────┐  ││
│  │  │ScreenCapture│  │ RegionSelect │  │ AutoInputHandler │  ││
│  │  │   Handler   │  │   Manager    │  │   (선택 기능)    │  ││
│  │  └──────┬──────┘  └──────────────┘  └──────────────────┘  ││
│  └─────────┼────────────────────────────────────────────────────┘│
│            │                                                     │
│  ┌─────────┴─────────────────────────────────────────────────┐  │
│  │                      Core Layer                            │  │
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────────┐ │  │
│  │  │  OCRService  │  │ QuizMatcher  │  │ DataRepository   │ │  │
│  │  │  (ML Kit)    │  │ (FuzzyWuzzy) │  │ (Room + CSV)     │ │  │
│  │  └──────────────┘  └──────────────┘  └──────────────────┘ │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │                    Data Layer                              │  │
│  │  ┌─────────────────────────────────────────────────────┐  │  │
│  │  │              Room Database (SQLite)                  │  │  │
│  │  │  - quiz_entries (카테고리별 퀴즈 데이터)             │  │  │
│  │  │  - quiz_fts (전문 검색 인덱스)                       │  │  │
│  │  │  - settings (사용자 설정)                            │  │  │
│  │  │  - regions (저장된 영역 정보)                        │  │  │
│  │  └─────────────────────────────────────────────────────┘  │  │
│  └───────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

### 3.2 데이터 흐름
```
[게임 화면]
     │
     ▼ (1) 접근성 서비스로 스크린샷
┌─────────────────┐
│ ScreenCapture   │
│ Handler         │
└────────┬────────┘
         │
         ▼ (2) 지정 영역 크롭
┌─────────────────┐
│ Region Manager  │
│ (좌표 계산)     │
└────────┬────────┘
         │
         ▼ (3) 이미지 전처리
┌─────────────────┐
│ ImagePreprocessor│
│ (확대+고대비)   │
└────────┬────────┘
         │
         ▼ (4) OCR 텍스트 추출
┌─────────────────┐
│ ML Kit OCR      │
│ (한국어 인식)   │
└────────┬────────┘
         │
         ▼ (5) 화면 분류
┌─────────────────┐
│ ScreenClassifier│
│ (문제/정답/기타)│
└────────┬────────┘
         │
         ▼ (6) 퍼지 매칭
┌─────────────────┐
│ QuizMatcher     │
│ (역색인+Fuzzy)  │
└────────┬────────┘
         │
         ▼ (7) 결과 표시
┌─────────────────┐
│ FloatingOverlay │
│ (정답 표시)     │
└────────┬────────┘
         │
         ▼ (8) 선택적 자동 입력
┌─────────────────┐
│ AutoInput       │
│ Handler         │
└─────────────────┘
```

---

## 4. 기술 스택

### 4.1 개발 환경
| 항목 | 선택 | 이유 |
|------|------|------|
| 언어 | **Kotlin** | 현대적 문법, 코루틴, Null 안전성 |
| 최소 SDK | **28 (Android 9)** | takeScreenshot API 지원 |
| 타겟 SDK | **34 (Android 14)** | 최신 보안 정책 준수 |
| 빌드 | Gradle (Kotlin DSL) | 표준 |

### 4.2 주요 라이브러리
```kotlin
dependencies {
    // Android Core
    implementation("androidx.core:core-ktx:1.12.0")
    implementation("androidx.appcompat:appcompat:1.6.1")
    implementation("com.google.android.material:material:1.11.0")

    // Lifecycle & ViewModel
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.7.0")
    implementation("androidx.lifecycle:lifecycle-viewmodel-ktx:2.7.0")

    // Room Database
    implementation("androidx.room:room-runtime:2.6.1")
    implementation("androidx.room:room-ktx:2.6.1")
    kapt("androidx.room:room-compiler:2.6.1")

    // ML Kit OCR
    implementation("com.google.mlkit:text-recognition-korean:16.0.0")

    // Fuzzy Matching
    implementation("me.xdrop:fuzzywuzzy:1.4.0")

    // Coroutines
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.7.3")

    // DataStore (설정 저장)
    implementation("androidx.datastore:datastore-preferences:1.0.0")

    // CSV Parser
    implementation("com.github.doyaaaaaken:kotlin-csv-jvm:1.9.2")
}
```

### 4.3 권한 요구사항
```xml
<manifest>
    <!-- 필수 권한 -->
    <uses-permission android:name="android.permission.SYSTEM_ALERT_WINDOW" />
    <uses-permission android:name="android.permission.FOREGROUND_SERVICE" />
    <uses-permission android:name="android.permission.FOREGROUND_SERVICE_SPECIAL_USE" />

    <!-- 접근성 서비스 (권한 아님, 사용자 설정 필요) -->
    <service
        android:name=".service.QuizAccessibilityService"
        android:permission="android.permission.BIND_ACCESSIBILITY_SERVICE"
        android:exported="false">
        <intent-filter>
            <action android:name="android.accessibilityservice.AccessibilityService" />
        </intent-filter>
        <meta-data
            android:name="android.accessibilityservice"
            android:resource="@xml/accessibility_config" />
    </service>
</manifest>
```

---

## 5. UI/UX 설계

### 5.1 화면 구성

#### 메인 화면 (MainActivity)
```
┌─────────────────────────────────────┐
│  ≡  QuizHelper            [설정]   │
├─────────────────────────────────────┤
│                                     │
│  ┌─────────────────────────────┐   │
│  │  🔴 접근성 서비스 비활성화   │   │
│  │     [설정으로 이동]         │   │
│  └─────────────────────────────┘   │
│                                     │
│  📂 카테고리 선택                   │
│  ┌─────────────────────────────┐   │
│  │  kkong (2,340 문제)      ▼ │   │
│  └─────────────────────────────┘   │
│                                     │
│  📱 영역 설정                       │
│  ┌─────────────────────────────┐   │
│  │  저장된 영역: 320x180       │   │
│  │  [영역 다시 선택]           │   │
│  └─────────────────────────────┘   │
│                                     │
│  ┌─────────────────────────────┐   │
│  │                             │   │
│  │      [ ▶ 시작하기 ]         │   │
│  │                             │   │
│  └─────────────────────────────┘   │
│                                     │
└─────────────────────────────────────┘
```

#### 영역 선택 오버레이
```
┌─────────────────────────────────────┐
│░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░│
│░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░│
│░░░░░░┌─────────────────────┐░░░░░░░│
│░░░░░░│                     │░░░░░░░│
│░░░░░░│   드래그하여 영역   │░░░░░░░│
│░░░░░░│      선택하세요     │░░░░░░░│
│░░░░░░│                     │░░░░░░░│
│░░░░░░└─────────────────────┘░░░░░░░│
│░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░│
│░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░│
│                                     │
│  [취소]                   [확인]   │
└─────────────────────────────────────┘
```

#### 결과 오버레이 (Floating)
```
확장 모드:
┌─────────────────────────────────────┐
│ ≡  QuizHelper          [_] [X]     │
├─────────────────────────────────────┤
│ 카테고리: kkong                  ▼ │
├─────────────────────────────────────┤
│                                     │
│ 이순신 장군이 전사한 해전은?        │
│                                     │
│ ✓ 노량해전                    [📋] │
│                                     │
│ ├ 명량해전 (82%)              [📋] │
│ └ 한산도대첩 (76%)            [📋] │
│                                     │
├─────────────────────────────────────┤
│ [■ 정지]          스캔 중... 2.1초 │
└─────────────────────────────────────┘

최소화 모드:
┌────────────────────────┐
│ ✓ 노량해전        [+] │
└────────────────────────┘
```

### 5.2 사용자 플로우
```
[앱 설치]
     │
     ▼
[첫 실행] → [접근성 서비스 활성화 안내] → [설정 화면으로 이동]
     │                                           │
     │                                           ▼
     │                                    [접근성 ON]
     │                                           │
     ▼◄──────────────────────────────────────────┘
[메인 화면]
     │
     ├─→ [카테고리 선택] → 데이터 로드
     │
     ├─→ [영역 선택] → 오버레이 표시 → 드래그 선택 → 저장
     │
     └─→ [시작하기]
              │
              ▼
     [게임으로 전환] + [오버레이 표시]
              │
              ▼
     [자동 스캔 시작] ←──────────────┐
              │                      │
              ▼                      │
     [OCR → 매칭 → 결과 표시] ───────┘
              │
              ▼
     [정답 탭] → [클립보드 복사] 또는 [자동 입력]
```

---

## 6. 성능 요구사항

### 6.1 목표 지표
| 항목 | 목표값 | 비고 |
|------|--------|------|
| OCR 인식 시간 | < 200ms | ML Kit 기준 |
| 매칭 검색 시간 | < 100ms | 1만 문제 기준 |
| 전체 응답 시간 | < 500ms | 캡처→결과표시 |
| 메모리 사용량 | < 150MB | 백그라운드 유지 |
| 배터리 소모 | < 5%/hour | 연속 스캔 시 |
| 스캔 주기 | 1.5~2초 | 설정 가능 |

### 6.2 최적화 전략
```
1. OCR 최적화
   - 변경 감지: 이전 이미지와 비교, 변화 없으면 OCR 스킵
   - 영역 크롭: 전체 화면 대신 선택 영역만 처리
   - 백그라운드 처리: Coroutine으로 비동기 실행

2. 매칭 최적화
   - 역색인: 키워드 기반 후보 필터링 (100개 이내)
   - 조기 종료: 100% 매칭 발견 시 즉시 반환
   - 캐싱: 최근 OCR 결과 캐시 (중복 검색 방지)

3. 메모리 최적화
   - Bitmap 재사용: 캡처/전처리 시 Bitmap Pool 사용
   - 데이터 페이징: 필요한 카테고리만 메모리 로드

4. 배터리 최적화
   - 적응형 스캔 주기: 매칭 성공 후 잠시 대기
   - 화면 꺼짐 감지: 화면 OFF 시 스캔 중지
   - Doze 모드 대응: Foreground Service 유지
```

---

## 7. 보안 및 제약사항

### 7.1 보안 고려사항
```
1. 데이터 보안
   - 퀴즈 DB: 앱 내부 저장소 (외부 접근 불가)
   - 설정 정보: EncryptedSharedPreferences 사용
   - 로그: Release 빌드에서 민감 정보 제거

2. 접근성 서비스 보안
   - 필요한 최소 권한만 요청
   - 화면 캡처 데이터 메모리에서만 처리 (저장 안 함)
   - 다른 앱 정보 수집 안 함

3. Play Store 정책
   - 접근성 서비스 사용 목적 명시
   - 개인정보 처리방침 필수
   - APK 직접 배포 권장 (심사 회피)
```

### 7.2 알려진 제약사항
```
1. 게임 호환성
   - 일부 게임은 FLAG_SECURE로 스크린샷 차단
   - 해결책: 해당 게임은 지원 불가 명시

2. Android 버전별 차이
   - Android 9-10: takeScreenshot 정상
   - Android 11+: 추가 권한 확인 필요
   - Android 14+: Foreground Service 타입 명시 필수

3. 제조사별 차이
   - 샤오미/화웨이: 배터리 최적화에서 제외 필요
   - 삼성: Game Launcher 간섭 가능
```

---

## 8. 개발 마일스톤

### Phase 1: 기반 구축 (1-2주)
```
□ 프로젝트 셋업 (Gradle, 의존성)
□ Room 데이터베이스 구조 설계
□ CSV 파싱 및 데이터 임포트
□ 기본 UI (MainActivity, Settings)
```

### Phase 2: 핵심 기능 (2-3주)
```
□ 접근성 서비스 구현
□ 화면 캡처 기능
□ 영역 선택 오버레이
□ ML Kit OCR 통합
□ 이미지 전처리
```

### Phase 3: 매칭 엔진 (1-2주)
```
□ 화면 분류 로직 (PC 버전 포팅)
□ 텍스트 정규화
□ 역색인 구현
□ 퍼지 매칭 (FuzzyWuzzy)
```

### Phase 4: UI/UX (1-2주)
```
□ 플로팅 오버레이 서비스
□ 결과 표시 UI
□ 최소화 모드
□ 클립보드 복사
□ 설정 화면
```

### Phase 5: 최적화 및 테스트 (1-2주)
```
□ 성능 최적화
□ 배터리 최적화
□ 다양한 기기 테스트
□ 게임 호환성 테스트
□ 버그 수정
```

### Phase 6: 배포 (1주)
```
□ APK 서명 및 빌드
□ 설치 가이드 작성
□ GitHub Release 또는 직접 배포
□ (선택) Play Store 등록
```

---

## 9. 디렉토리 구조
```
app/
├── src/main/
│   ├── java/com/quizhelper/
│   │   ├── MainActivity.kt
│   │   ├── SettingsActivity.kt
│   │   │
│   │   ├── service/
│   │   │   ├── QuizAccessibilityService.kt
│   │   │   ├── FloatingOverlayService.kt
│   │   │   └── ScreenCaptureHandler.kt
│   │   │
│   │   ├── ocr/
│   │   │   ├── OcrService.kt
│   │   │   └── ImagePreprocessor.kt
│   │   │
│   │   ├── matcher/
│   │   │   ├── QuizMatcher.kt
│   │   │   ├── ScreenClassifier.kt
│   │   │   └── TextNormalizer.kt
│   │   │
│   │   ├── data/
│   │   │   ├── QuizDatabase.kt
│   │   │   ├── QuizDao.kt
│   │   │   ├── QuizEntry.kt
│   │   │   ├── QuizRepository.kt
│   │   │   └── CsvImporter.kt
│   │   │
│   │   ├── ui/
│   │   │   ├── overlay/
│   │   │   │   ├── FloatingView.kt
│   │   │   │   ├── RegionSelectView.kt
│   │   │   │   └── BorderView.kt
│   │   │   └── components/
│   │   │       └── ResultCard.kt
│   │   │
│   │   └── util/
│   │       ├── PermissionHelper.kt
│   │       ├── ClipboardHelper.kt
│   │       └── Constants.kt
│   │
│   ├── res/
│   │   ├── layout/
│   │   ├── drawable/
│   │   ├── values/
│   │   └── xml/
│   │       └── accessibility_config.xml
│   │
│   └── assets/
│       └── data/
│           ├── kkong.csv
│           └── ...
│
├── build.gradle.kts
└── proguard-rules.pro
```

---

## 10. 리스크 및 대응

| 리스크 | 확률 | 영향 | 대응 방안 |
|--------|------|------|----------|
| 게임 스크린샷 차단 | 중 | 높음 | 지원 게임 목록 명시, MediaProjection 대안 검토 |
| Play Store 심사 거부 | 높음 | 중 | APK 직접 배포, GitHub Releases 활용 |
| 접근성 서비스 복잡성 | 중 | 중 | 단계별 개발, 충분한 테스트 |
| 기기별 호환성 | 중 | 중 | 다양한 기기 테스트, 사용자 피드백 반영 |
| OCR 정확도 부족 | 낮 | 중 | 전처리 강화, 사용자 피드백 학습 |

---

## 11. 향후 확장 계획

### v1.1 (추후)
- 자동 입력 기능 안정화
- 위젯 지원
- 음성 안내 (정답 읽어주기)

### v1.2 (추후)
- 서버 동기화 (퀴즈 DB 업데이트)
- 사용자 기여 (새 문제 추가)
- 통계 대시보드

### v2.0 (추후)
- iOS 지원 검토 (제한적)
- 멀티 게임 프로필
- AI 기반 새 문제 학습

---

## 12. 결론

Android QuizHelper는 현재 PC 버전의 모든 핵심 기능을 모바일로 이식할 수 있으며, 일부 기능(자동 입력)은 오히려 더 강력하게 구현 가능합니다.

**핵심 기술적 도전:**
1. 접근성 서비스 구현 (중간 난이도)
2. 플로팅 오버레이 UI (중간 난이도)
3. 다양한 기기 호환성 (테스트 필요)

**예상 개발 기간:** 6-10주 (1인 개발 기준)

**배포 전략:** APK 직접 배포 권장 (Play Store 심사 리스크 회피)
