from PIL import Image

# PNG 파일을 ICO 형식으로 변환
input_path = r"C:\Users\taehe\OneDrive\문서\GitHub\qsearch_exe\QuizHelper\icon.png"
output_path = r"C:\Users\taehe\OneDrive\문서\GitHub\qsearch_exe\QuizHelper\icon.ico"

# 이미지 열기
img = Image.open(input_path)

# ICO 파일로 저장 (여러 크기 포함)
img.save(output_path, format='ICO', sizes=[(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])

print(f"✓ ICO 파일 생성 완료: {output_path}")
