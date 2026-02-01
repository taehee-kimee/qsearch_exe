---
description: QuizHelper 빌드 및 배포 워크플로우 - 최종 배포 파일을 Releases 폴더에 생성
---

# QuizHelper 빌드 워크플로우

사용자가 "빌드해줘" 또는 이와 유사한 요청을 하면, 아래 단계를 **반드시** 순서대로 실행하세요.

## 필수 변수
- **프로젝트 경로**: `c:\Users\taehe\OneDrive\문서\GitHub\qsearch_exe`
- **Releases 폴더**: `c:\Users\taehe\OneDrive\문서\GitHub\qsearch_exe\Releases`
- **현재 버전**: `releases.win.json` 파일에서 확인 가능

---

## 빌드 단계

### 1단계: 클린 빌드
// turbo
```powershell
dotnet clean QuizHelper/QuizHelper.csproj -c Release
```

### 2단계: Release 빌드
// turbo
```powershell
dotnet build QuizHelper/QuizHelper.csproj -c Release
```

### 3단계: Publish (배포용 빌드)
// turbo
```powershell
dotnet publish QuizHelper/QuizHelper.csproj -c Release -o QuizHelper/publish --self-contained false
```

### 4단계: 버전 확인
`Releases/releases.win.json` 파일에서 현재 버전을 확인합니다.
버전은 자동으로 0.0.1씩 증가시킵니다. (예: 1.0.5 → 1.0.6)

### 5단계: Velopack 패키징
버전 번호를 증가시킨 후 실행:
```powershell
vpk pack --packId QuizHelper --packVersion <새버전> --packDir QuizHelper/publish --mainExe QuizHelper.exe --outputDir Releases --icon QuizHelper/icon.ico
```

예시 (버전 1.0.6인 경우):
```powershell
vpk pack --packId QuizHelper --packVersion 1.0.6 --packDir QuizHelper/publish --mainExe QuizHelper.exe --outputDir Releases --icon QuizHelper/icon.ico
```

---

## 최종 결과물 (Releases 폴더)

빌드 완료 후 Releases 폴더에 다음 파일들이 생성됩니다:
- `QuizHelper-<버전>-full.nupkg` - Velopack 업데이트 패키지
- `QuizHelper-win-Portable.zip` - 포터블 버전
- `QuizHelper-win-Setup.exe` - 설치 프로그램
- `RELEASES` - 릴리즈 메타데이터
- `releases.win.json` - 버전 정보

---

## 주의사항
1. **Releases 폴더만** 최종 배포 파일을 포함합니다.
2. `QuizHelper/bin/`, `QuizHelper/obj/`, `QuizHelper/publish/` 폴더는 임시 파일입니다.
3. 빌드 전 이전 버전 파일은 덮어쓰기 됩니다.
4. vpk 명령어가 없으면 `dotnet tool install -g vpk` 로 설치하세요.
