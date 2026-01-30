from PIL import Image
import os

# 입력 파일 경로
input_path = r"C:\Users\taehe\.gemini\antigravity\brain\23b45937-6cc3-49f1-af73-f5f058f7a23e\uploaded_media_1769762103698.jpg"
output_path = r"C:\Users\taehe\OneDrive\문서\GitHub\qsearch_exe\QuizHelper\icon.png"

# 이미지 열기
img = Image.open(input_path)

# RGB로 변환 (RGBA가 아닌 경우)
img = img.convert("RGBA")

# 픽셀 데이터 가져오기
datas = img.getdata()

new_data = []
for item in datas:
    # 흰색 또는 거의 흰색인 픽셀을 투명하게 변경
    # RGB 값이 모두 240 이상인 경우 투명하게 처리
    if item[0] > 240 and item[1] > 240 and item[2] > 240:
        new_data.append((255, 255, 255, 0))  # 투명
    else:
        new_data.append(item)

# 새 데이터 적용
img.putdata(new_data)

# PNG로 저장
img.save(output_path, "PNG")
print(f"✓ 투명 배경 이미지 생성 완료: {output_path}")
print(f"✓ 이미지 크기: {img.size}")
