---
description: CSV 데이터 정규화 워크플로우 - 퀴즈 데이터 파일을 탭 구분자 형식으로 정규화
---

# CSV 데이터 정규화 워크플로우

사용자가 "문제 업데이트", "CSV 정규화", "데이터 정리" 또는 이와 유사한 요청을 하면, 아래 단계를 **반드시** 순서대로 실행하세요.

## 필수 변수
- **프로젝트 경로**: `c:\Users\taehe\OneDrive\문서\GitHub\qsearch_exe`
- **데이터 폴더**: `c:\Users\taehe\OneDrive\문서\GitHub\qsearch_exe\QuizHelper\data`
- **정규화 스크립트**: `c:\Users\taehe\OneDrive\문서\GitHub\qsearch_exe\scripts\normalize_csv.ps1`

---

## 정규화 단계

### 1단계: 현재 데이터 파일 확인
// turbo
```powershell
Get-ChildItem -Path "QuizHelper/data" -Filter "*.csv" | Select-Object Name, Length, LastWriteTime
```

### 2단계: CSV 정규화 스크립트 실행
// turbo
```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\normalize_csv.ps1"
```

### 3단계: 결과 확인
스크립트 실행 결과를 확인합니다:
- **Lines converted**: 탭으로 변환된 줄 수
- **Files already normalized**: 이미 정규화된 파일 수
- **Errors**: 오류 발생 시 표시

---

## CSV 파일 형식 규칙

### 올바른 형식
```
Question	Answer
질문 내용?	정답
```

### 규칙
1. 첫 번째 줄은 헤더: `Question\tAnswer`
2. 각 줄은 질문과 답이 **탭(Tab)**으로 구분
3. 질문은 `?`로 끝남
4. 빈 줄은 허용되지 않음

---

## 데이터 파일 목록

| 파일명 | 설명 |
|--------|------|
| `ollaolla.csv` | 올라올라 퀴즈 데이터 |
| `garosero.csv` | 가로세로 퀴즈 데이터 |
| 기타 `.csv` 파일 | 추가 퀴즈 데이터 |

---

## 수동으로 문제 추가 시

새 문제를 추가할 때 다음 형식을 따르세요:

```
새로운 질문 내용?	정답
```

**주의사항:**
- 질문과 답 사이에 **탭(Tab)** 사용 (스페이스 X)
- 질문 끝에 **물음표(?)** 필수
- 특수문자는 그대로 사용 가능

---

## 문제 해결

### 탭이 아닌 다른 구분자로 되어 있는 경우
→ 스크립트가 자동으로 `?` 위치를 찾아 탭으로 변환

### 변환 실패 시
→ 해당 줄 번호와 내용이 Warning으로 출력됨
→ 수동으로 확인 후 수정 필요
