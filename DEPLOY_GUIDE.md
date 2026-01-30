========================================
GitHub Pages 배포 가이드
========================================

이 가이드는 큐플족보 문제 검색 시스템을 GitHub Pages에 배포하는 방법을 안내합니다.

## 사전 준비

### 1. Python 환경 설정

Python 3.7 이상이 설치되어 있어야 합니다.

```bash
python --version
```

### 2. 필요한 패키지 설치

```bash
pip install -r requirements.txt
```

또는

```bash
pip install openpyxl
```

## 배포 단계

### 1단계: Excel 파일을 JSON으로 변환

프로젝트 루트 디렉토리에서 다음 명령어를 실행합니다:

```bash
python convert_excel_to_json.py
```

또는 특정 파일을 지정하려면:

```bash
python convert_excel_to_json.py 큐플족보_251021.xlsx
```

출력 파일 위치를 지정하려면:

```bash
python convert_excel_to_json.py 큐플족보_251021.xlsx data.json
```

변환이 완료되면 `data.json` 파일이 생성됩니다.

### 2단계: GitHub 저장소 생성 및 파일 업로드

1. GitHub에 새로운 저장소를 생성합니다.
   - 저장소 이름: 원하는 이름 (예: `qplay-search`)
   - Public 또는 Private 선택 가능
   - README, .gitignore, license는 선택사항

2. 다음 파일들을 저장소에 업로드합니다:
   - `index.html`
   - `data.json` (변환된 JSON 파일)
   - `.nojekyll`
   - `convert_excel_to_json.py` (선택사항, 나중에 업데이트할 때 사용)
   - `requirements.txt` (선택사항)

   **참고**: `큐플족보_251021.xlsx` 파일은 업로드하지 않아도 됩니다. 
   JSON 파일이 있으면 자동으로 로드됩니다. Excel 파일은 백업용으로만 유지하세요.

### 3단계: GitHub Pages 활성화

1. GitHub 저장소 페이지로 이동합니다.

2. Settings 탭을 클릭합니다.

3. 왼쪽 사이드바에서 "Pages"를 클릭합니다.

4. "Source" 섹션에서:
   - Branch를 선택합니다 (기본: `main` 또는 `master`)
   - Folder를 `/ (root)`로 선택합니다.

5. "Save" 버튼을 클릭합니다.

6. 몇 분 후 웹사이트가 활성화됩니다.
   - URL 형식: `https://[사용자명].github.io/[저장소명]/`

### 4단계: 확인

배포가 완료되면 브라우저에서 다음 URL로 접속하여 확인합니다:

```
https://[사용자명].github.io/[저장소명]/
```

예: `https://username.github.io/qplay-search/`

## 업데이트 방법

Excel 파일이 업데이트된 경우:

1. 로컬에서 Excel 파일을 JSON으로 다시 변환합니다:
   ```bash
   python convert_excel_to_json.py 큐플족보_251021.xlsx
   ```

2. GitHub 저장소에 `data.json` 파일을 업로드/커밋합니다:
   ```bash
   git add data.json
   git commit -m "Update data.json"
   git push
   ```

3. GitHub Pages는 자동으로 업데이트됩니다 (몇 분 소요).

## 로컬 테스트 방법

GitHub Pages에 배포하기 전에 로컬에서 테스트할 수 있습니다:

### 방법 1: Python HTTP 서버 사용

```bash
# Python 3
python -m http.server 8000

# 또는 Python 2
python -m SimpleHTTPServer 8000
```

브라우저에서 `http://localhost:8000` 접속

### 방법 2: Flask 서버 사용 (이미 있는 경우)

```bash
python server.py
```

브라우저에서 `http://localhost:5000` 접속

## 파일 구조

배포 후 최종 파일 구조:

```
qplay/
├── index.html          # 메인 HTML 파일
├── data.json          # 변환된 질문/답 데이터 (필수)
├── .nojekyll          # Jekyll 처리 방지 파일 (필수)
├── convert_excel_to_json.py  # 변환 스크립트 (선택사항)
├── requirements.txt   # Python 의존성 (선택사항)
└── 큐플족보_251021.xlsx  # 원본 Excel 파일 (선택사항, 백업용)
```

## 문제 해결

### JSON 파일이 로드되지 않는 경우

1. `data.json` 파일이 저장소 루트에 있는지 확인
2. 파일 이름이 정확히 `data.json`인지 확인 (대소문자 구분)
3. 브라우저 개발자 도구(F12)의 Network 탭에서 404 오류 확인

### Excel 파일이 자동으로 로드되지 않는 경우

- JSON 파일(`data.json`)이 있으면 우선적으로 JSON을 로드합니다.
- Excel 파일을 사용하려면 JSON 파일을 삭제하거나, 파일 업로드 기능을 사용하세요.

### GitHub Pages가 업데이트되지 않는 경우

1. 저장소 Settings > Pages에서 배포 상태 확인
2. Actions 탭에서 배포 로그 확인
3. 브라우저 캐시 삭제 후 다시 시도

## 추가 정보

- GitHub Pages는 정적 웹사이트만 지원합니다 (서버 사이드 코드 실행 불가)
- 모든 데이터는 클라이언트 측(브라우저)에서 처리됩니다
- Excel 파일을 직접 호스팅할 수도 있지만, JSON 파일이 더 빠르고 효율적입니다

## 참고 사항

- Excel 파일이 크면 JSON 파일도 상당히 커질 수 있습니다.
- 매우 큰 파일의 경우 GitHub LFS를 사용하거나 다른 호스팅 서비스를 고려하세요.

========================================
QuizHelper 앱 업데이트 가이드
========================================

이 섹션은 윈도우용 설치 프로그램(QuizHelper)의 수정 및 배포 방법을 안내합니다.

## 프로젝트 구조 이해

*   **웹사이트 프로젝트** (`qplay_search`): 웹사이트 소스 코드와 배포 가이드가 있는 곳입니다. GitHub Releases를 통해 앱 설치 파일을 제공합니다.
*   **앱 프로젝트** (`qsearch_exe`): 실제 C# WPF 프로그램 소스 코드가 있는 곳입니다. **기능 수정은 여기서 해야 합니다.**

## 업데이트 워크플로우

### 1단계: 코드 수정 (앱 프로젝트)

1.  파일 탐색기나 VS Code에서 앱 프로젝트 폴더로 이동합니다.
    *   위치: `.../GitHub/qsearch_exe/QuizHelper`
2.  C# 소스 코드(`.cs`, `.xaml`)를 수정하여 기능을 개선하거나 버그를 전합니다.

### 2단계: 버전 올리기 및 패키징

터미널(PowerShell)을 열고 앱 프로젝트 폴더(`qsearch_exe/QuizHelper`)에서 다음 명령어를 순서대로 실행합니다.

**1. 릴리즈 모드로 빌드**
```powershell
dotnet build -c Release
```

**2. 패키징 (버전 변경 필수!)**
*   `-v` 뒤에 새로운 버전 번호를 입력하세요 (예: 1.0.3 -> 1.0.4)
*   **주의**: 버전은 항상 이전보다 높아야 자동 업데이트가 작동합니다.
```powershell
vpk pack -u QuizHelper -v 1.0.4 -p bin\Release\net6.0-windows10.0.19041.0 -e QuizHelper.exe
```

### 3단계: 배포 (GitHub Releases)

웹사이트 프로젝트(`qplay_search`)의 GitHub 저장소에 새 버전을 업로드합니다.

1.  GitHub 저장소의 Releases 페이지로 이동:
    *   https://github.com/taehee-kimee/qplay_search/releases
2.  "Draft a new release" 클릭
3.  **Tag version**: `v1.0.4` (패키징할 때 쓴 버전과 동일하게)
4.  **Release title**: `QuizHelper v1.0.4`
5.  **Files**: `qsearch_exe/QuizHelper/Releases` 폴더에 생성된 다음 파일들을 업로드합니다:
    *   `QuizHelper-win-Setup.exe` (필수)
    *   `QuizHelper-1.0.4-full.nupkg` (필수 - 자동 업데이트용)
    *   `RELEASES` (필수 - 버전 정보)
    *   `QuizHelper-1.0.4-delta.nupkg` (선택 - 용량 절약용)
6.  "Publish release" 클릭

### 4단계: 완료

이제 사용자들이 웹사이트에서 다운로드하거나, 앱을 켜면 자동으로 업데이트됩니다.

> **참고**: 웹사이트(`index.html`)의 다운로드 링크는 `releases/latest`를 가리키므로, 별도로 웹사이트 코드를 수정할 필요가 없습니다.
