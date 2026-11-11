"""수집한 문제를 족보(설치형 + 웹)에 자동으로 합치는 루틴.

읽는 곳
  - 설치형: %APPDATA%\\QuizHelper\\collected\\<카테고리>.csv   (질문\\t답\\t메모)
  - 웹:     ~/Downloads/모은문제_*.tsv                      (카테고리\\t질문\\t답\\t메모)
  - 공유:   https://qplay-search.vercel.app/api/collect      (모든 웹 사용자가 보낸 수집분)
            다른 사람이 보낸 문제는 서로 다른 2명 이상이 같은 답을 보내야 자동으로 합친다.
            1명만 보낸 문제는 기다렸다가 14일이 지나면 검토필요.tsv 로 넘긴다.

하는 일
  1. 두 저장소를 최신으로 받는다 (git pull --ff-only)
  2. 깨끗한 항목만 골라 qsearch_exe/QuizHelper/data/<카테고리>.csv 에 추가한다
     - 답 끝에 " -수집-" 표시 (앱의 답 복사 시 자동으로 떨어짐)
     - OCR이 받침·자음 하나 틀린 답은 족보에 이미 있는 표기로 고친다 (노량대접 → 노량대첩)
  3. 애매한 항목은 합치지 않고 문서\\QuizHelper 수집\\검토필요.tsv 에 남긴다 (로그·상태 파일도 같은 폴더)
  4. 합친 족보를 웹(qplay_search/categories)에 복사하고 metadata.json 문제 수를 갱신한다
  5. --push 이면 두 저장소에 커밋·푸시한다 (웹은 푸시 즉시 사이트에 반영)

한 번 처리한 항목은 merge_state.json 에 기록해 다시 처리하지 않는다.
설치형 앱 사용자에게 새 족보를 보내려면 따로 버전을 올려 릴리스해야 한다.

사용법
  python scripts/merge_collected.py            # 미리보기 (파일을 바꾸지 않음)
  python scripts/merge_collected.py --apply    # 족보 파일에 반영
  python scripts/merge_collected.py --push     # 반영 + 두 저장소 커밋/푸시
"""

import argparse
import datetime as dt
import difflib
import glob
import json
import os
import re
import shutil
import subprocess
import sys

HOME = os.path.expanduser('~')
# 테스트용으로 경로를 바꿀 수 있다 (QH_DESKTOP_REPO, QH_WEB_REPO, QH_COLLECTED_DIR, QH_WORK_DIR, QH_DOWNLOADS_GLOB)
DESKTOP_REPO = os.environ.get('QH_DESKTOP_REPO') or os.path.join(HOME, 'qsearch_exe')
DATA_DIR = os.path.join(DESKTOP_REPO, 'QuizHelper', 'data')
WEB_REPO = os.environ.get('QH_WEB_REPO') or os.path.join(HOME, 'Documents', 'GitHub', 'qplay_search')
WEB_CATEGORIES = os.path.join(WEB_REPO, 'categories')
WEB_METADATA = os.path.join(WEB_REPO, 'metadata.json')
COLLECTED_DIR = os.environ.get('QH_COLLECTED_DIR') or os.path.join(os.environ.get('APPDATA', HOME), 'QuizHelper', 'collected')
# 루틴이 쓰는 파일(상태·로그·검토 목록)은 %APPDATA% 밖에 둔다.
# Microsoft Store 판 Python은 %APPDATA%에 쓰는 파일을 몰래 앱 전용 폴더로 돌려 저장하기 때문.
WORK_DIR = os.environ.get('QH_WORK_DIR') or os.path.join(HOME, 'Documents', 'QuizHelper 수집')
IMPORTED_DIR = os.path.join(WORK_DIR, 'imported')
STATE_PATH = os.path.join(WORK_DIR, 'merge_state.json')
REVIEW_PATH = os.path.join(WORK_DIR, '검토필요.tsv')
LOG_PATH = os.path.join(WORK_DIR, 'merge_log.txt')
REPORT_PATH = os.path.join(WORK_DIR, '수집현황.html')
# 여기 적힌 웹 브라우저(수집기 client id)가 보낸 문제는 1명이어도 바로 합친다 (관리자 본인 브라우저 등)
TRUSTED_CLIENTS_PATH = os.path.join(WORK_DIR, '믿을수있는_수집자.txt')
REFRESH_CMD_PATH = os.path.join(WORK_DIR, '수집현황 새로고침.cmd')
DOWNLOADS_GLOB = os.environ.get('QH_DOWNLOADS_GLOB') or os.path.join(HOME, 'Downloads', '모은문제_*.tsv')
REMOTE_URL = os.environ.get('QH_REMOTE_URL') or 'https://qplay-search.vercel.app/api/collect'
ADMIN_KEY_FILE = os.environ.get('QH_ADMIN_KEY_FILE') or os.path.join(os.environ.get('APPDATA', HOME), 'QuizHelper', 'collect_admin_key.txt')
# 클라우드(GitHub Actions '족보 바로 반영')에서 돌 때: 검토 항목은 파일 대신 상태에 쌓아 두고 PC 루틴이 꺼내 간다
IN_CI = bool(os.environ.get('QH_CI'))
REMOTE_MIN_SOURCES = 2      # 다른 사람이 보낸 문제는 이만큼 서로 다른 사람이 같은 답을 보내야 합침
REMOTE_WAIT_DAYS = 14       # 1명만 보낸 문제를 기다리는 기간

CATEGORIES = ['garosero', 'kkong', 'ollaolla', 'ox_xo']
# 웹 카테고리 이름 → 파일 이름
WEB_SHEET_TO_FILE = {'가로세로': 'garosero', '꽁꽁': 'kkong', '올라올라(꼬로록)': 'ollaolla', 'ox,xo': 'ox_xo'}

COLLECT_TAG = ' -수집-'
OK_CHARS = r"가-힣a-zA-Z0-9\s?？.,'\"‘’“”()<>\[\]〈〉《》『』「」!~·:%/\-&+=_#"
GARBAGE_RE = re.compile(f'[^{OK_CHARS}]')
# 화면의 출제자 표시가 질문 사이에 끼어든 경우 ("…원Ai나인 :큐플레이 분위기…")
#   "큐플레이"라는 단어 자체는 정상 질문에도 나오므로, 콜론 뒤에 붙은 경우만 본다
OVERLAY_RE = re.compile(r'[:：]\s*\S{0,3}[플폴]레이|(출제자|춘저|수제자|수세\s*사)\s*[:：]')
# 한글 사이에 소문자가 섞인 영문 조각 ("원Ai나인"). "비타민A와", "OO", "LG트윈스" 같은 정상 표기는 해당 없음
SANDWICH_RE = re.compile(r'[가-힣](?:[a-z]{1,2}|[A-Z][a-z])[가-힣]')

CATEGORY_LABELS = {'garosero': '가로세로', 'kkong': '꽁꽁', 'ollaolla': '꼬로록/올라', 'ox_xo': 'OX'}

_log_lines = []


def log(msg=''):
    try:
        print(msg)
    except UnicodeEncodeError:  # 콘솔 인코딩이 한글을 못 쓰는 경우
        print(msg.encode('ascii', 'replace').decode())
    _log_lines.append(msg)


def notify(title, body):
    """Windows 알림 (화면 오른쪽 아래). 실패해도 루틴은 계속."""
    if os.name != 'nt' or os.environ.get('QH_NO_NOTIFY'):
        return
    ps = (
        "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null;"
        "[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] > $null;"
        "$x = New-Object Windows.Data.Xml.Dom.XmlDocument;"
        "$x.LoadXml('<toast><visual><binding template=\"ToastGeneric\"><text></text><text></text></binding></visual></toast>');"
        "$t = $x.GetElementsByTagName('text');"
        "$t.Item(0).AppendChild($x.CreateTextNode($env:QH_TITLE)) > $null;"
        "$t.Item(1).AppendChild($x.CreateTextNode($env:QH_BODY)) > $null;"
        "$app = '{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\\WindowsPowerShell\\v1.0\\powershell.exe';"
        "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($app).Show([Windows.UI.Notifications.ToastNotification]::new($x))"
    )
    env = dict(os.environ, QH_TITLE=title, QH_BODY=body)
    try:
        subprocess.run(['powershell', '-NoProfile', '-NonInteractive', '-Command', ps],
                       env=env, capture_output=True, timeout=30,
                       creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
    except Exception:
        pass


# ===== 족보 파일 읽기 (앱/웹 로더와 같은 규칙) =====

def load_bank(path):
    rows = []
    if not os.path.exists(path):
        return rows
    with open(path, encoding='utf-8-sig') as f:
        for i, line in enumerate(f.read().split('\n')):
            line = line.rstrip('\r')
            if not line.strip():
                continue
            if i == 0 and re.search(r'question|answer|질문', line, re.I):
                continue
            if '\t' in line:
                q, a = line.split('\t')[:2]
            else:
                k = line.rfind('?')
                if k < 0:
                    continue
                q, a = line[:k + 1], line[k + 1:]
            q, a = q.strip(), a.strip()
            if q and a:
                rows.append((q, a))
    return rows


def qkey(s):
    return re.sub(r'[^0-9a-zA-Z가-힣]', '', s).lower()


def tidy_text(text, question=False):
    """OCR이 한글을 한 글자씩 띄어 읽은 것("지 진 계")을 붙이고, 질문 끝 부스러기("…장치는? 게")를 지운다."""
    text = re.sub(r'\s+', ' ', str(text or '')).strip()
    if question:
        # "[문제 5]" 표시의 꼬리만 남은 경우 ("5] 고구려 고분벽화에…")
        text = re.sub(r'^\d{1,2}\s*[\]>|]\s*', '', text)
        # 붙이기 전에 먼저: 물음표 뒤에 보기가 통째로 붙어 온 경우("…대회는? 메 1. 1972 뮌헨 2. …") 물음표까지만
        text = re.sub(r'([?？])\s*\S{0,2}\s*[1-4](?:\s*[.,)]|\s).*$', r'\1', text)
    tokens = [t for t in text.split(' ') if re.search('[가-힣]', t)]
    min_tokens = 3 if question else 2   # 답은 짧으니 "박 쥐"처럼 두 글자도 붙인다
    if len(tokens) >= min_tokens and sum(1 for t in tokens if re.fullmatch('[가-힣]', t)) / len(tokens) >= 0.6:
        text = re.sub(r'([가-힣]) (?=[가-힣](?: |$|[?.,!)]))', r'\1', text)
        # "1 8 6 7" → "1867"
        text = re.sub(r'(^|\s)(\d(?:\s\d)+)(?=\s|$|[가-힣])', lambda m: m.group(1) + m.group(2).replace(' ', ''), text)
    # 기호 주변 공백 ("티그리스 , 유프라테스" → "티그리스, 유프라테스", "( 독일전 )" → "(독일전)", "' 푸른바다 '" → "'푸른바다'")
    text = re.sub(r'\s+([,)\]])', r'\1', text)
    text = re.sub(r'([(\[])\s+', r'\1', text)
    text = re.sub(r"'\s*([^']*?)\s*'", r"'\1'", text)
    if not question:
        text = re.sub(r'[\s\-–_|>]+$', '', text)  # 답 끝의 "-", "|" 같은 부스러기
    if question:
        # 물음표 뒤 짧은 부스러기 ("…쓰는것은? 로 Q")
        text = re.sub(r'([?？])\s*(?:\S\s*){1,4}$', r'\1', text)
        text = re.sub(r'\s+([?？])', r'\1', text)
    return re.sub(r'\s+', ' ', text).strip()


def load_trusted_clients():
    # 클라우드에서는 GitHub 비밀값 QH_TRUSTED_CLIENTS(쉼표·줄바꿈 구분)로 받는다
    ids = {x.strip() for x in re.split(r'[,\s]+', os.environ.get('QH_TRUSTED_CLIENTS', '')) if x.strip()}
    try:
        with open(TRUSTED_CLIENTS_PATH, encoding='utf-8-sig') as f:
            ids |= {ln.strip() for ln in f if ln.strip() and not ln.lstrip().startswith('#')}
    except OSError:
        pass
    return ids


def admin_key():
    key = os.environ.get('QH_ADMIN_KEY', '').strip()
    if key:
        return key
    try:
        return open(ADMIN_KEY_FILE, encoding='utf-8').read().strip()
    except OSError:
        return ''



def answer_core(a):
    """닉네임·별표 꼬리를 뗀 순수 답"""
    a = a.split('/')[0]
    a = re.split(r'[★☆*※•▪▫]', a)[0]
    a = re.split(r'\s-', a)[0]
    return a.strip()


def akey(a):
    return qkey(answer_core(a))


def to_jamo(text):
    out = []
    for ch in text:
        c = ord(ch) - 0xAC00
        if 0 <= c <= 11171:
            out.append(chr(0x1100 + c // 588))
            out.append(chr(0x1161 + (c % 588) // 28))
            if c % 28:
                out.append(chr(0x11A7 + c % 28))
        else:
            out.append(ch)
    return ''.join(out)


def jamo_ratio(a, b):
    return difflib.SequenceMatcher(None, to_jamo(a), to_jamo(b)).ratio() * 100


def garbage_ratio(s):
    compact = re.sub(r'\s', '', s)
    if not compact:
        return 1.0
    return len(GARBAGE_RE.findall(compact)) / len(compact)


# ===== 수집 항목 읽기 =====

def read_desktop_collected():
    items = []
    for name in CATEGORIES:
        path = os.path.join(COLLECTED_DIR, f'{name}.csv')
        if not os.path.exists(path):
            continue
        with open(path, encoding='utf-8-sig') as f:
            for i, line in enumerate(f.read().split('\n')):
                line = line.rstrip('\r')
                if not line.strip() or i == 0:
                    continue
                parts = line.split('\t')
                if len(parts) >= 2:
                    items.append({'cat': name, 'q': parts[0].strip(), 'a': parts[1].strip(),
                                  'note': parts[2].strip() if len(parts) > 2 else '', 'src': '설치형'})
    return items


def read_web_exports():
    items, files = [], sorted(glob.glob(DOWNLOADS_GLOB))
    for path in files:
        with open(path, encoding='utf-8-sig') as f:
            for i, line in enumerate(f.read().split('\n')):
                line = line.rstrip('\r')
                if not line.strip() or i == 0:
                    continue
                parts = line.split('\t')
                if len(parts) >= 3 and parts[0].strip() in WEB_SHEET_TO_FILE:
                    items.append({'cat': WEB_SHEET_TO_FILE[parts[0].strip()], 'q': parts[1].strip(),
                                  'a': parts[2].strip(), 'note': parts[3].strip() if len(parts) > 3 else '',
                                  'src': '웹'})
    return items, files


# ===== 공유 서버 (모든 웹 사용자 수집분) =====

def http_json(url, timeout=30):
    import urllib.request
    import urllib.error
    req = urllib.request.Request(url, headers={'User-Agent': 'qplay-merge-routine'})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.status, json.loads(r.read().decode('utf-8'))
    except urllib.error.HTTPError as e:
        try:
            return e.code, json.loads(e.read().decode('utf-8'))
        except ValueError:
            return e.code, {}
    except Exception as e:  # 네트워크 오류
        return 0, {'error': str(e)}


def fetch_remote(state, apply):
    """서버의 새 제출을 읽어 대기 목록에 쌓고, 합칠 준비가 된 항목과 검토로 넘길 항목을 돌려준다."""
    import urllib.parse
    seen = set(state.get('remote_seen', []))
    pending = dict(state.get('remote_pending', {}))
    key = admin_key()
    if not key:
        log('공유 서버: 관리자 키 파일이 없어 건너뜀')
        return [], [], pending

    new_blobs, cursor = [], None
    while True:
        query = {'key': key}
        if cursor:
            query['cursor'] = cursor
        status, body = http_json(f'{REMOTE_URL}?{urllib.parse.urlencode(query)}')
        if status == 503:
            log('공유 서버: 저장소(Vercel Blob)가 아직 연결되지 않아 건너뜀')
            return [], [], pending
        if status != 200 or not body.get('ok'):
            log(f'공유 서버: 읽기 실패 ({status} {body.get("error", "")})')
            return [], [], pending
        new_blobs += [b for b in body.get('blobs', []) if b['pathname'] not in seen]
        if not body.get('hasMore'):
            break
        cursor = body.get('cursor')

    now = dt.datetime.now(dt.timezone.utc)
    trusted = load_trusted_clients()
    # 보낸 순서대로 처리해야 '보냄 → 지움(취소) → 다시 모음'이 올바르게 반영된다 (파일 이름 = 날짜/밀리초)
    new_blobs.sort(key=lambda b: b['pathname'])
    retracted = 0
    for b in new_blobs:
        status, rec = http_json(b['url'])
        if status != 200:
            continue
        source = str(rec.get('source', ''))[:32] or 'unknown'
        is_trusted = str(rec.get('client', '')) in trusted
        is_retract = rec.get('retract') is True
        for it in rec.get('items', []):
            cat = WEB_SHEET_TO_FILE.get(it.get('sheet'))
            q = tidy_text(it.get('question', ''), question=True)
            a = tidy_text(it.get('answer', ''))
            if not cat or not q or not a:
                continue
            k = f'{cat}|{qkey(q)}|{akey(a)}'
            if is_retract:
                # 사용자가 '모은 문제'에서 지움 → 그 사람이 보낸 몫만 취소 (다른 사람 제출은 그대로)
                entry = pending.get(k)
                if entry:
                    if source in entry['sources']:
                        entry['sources'].remove(source)
                    if is_trusted:
                        entry.pop('trusted', None)
                    if not entry['sources']:
                        del pending[k]
                    retracted += 1
                continue
            entry = pending.setdefault(k, {'cat': cat, 'q': q, 'a': a, 'note': str(it.get('note', ''))[:80],
                                           'sources': [], 'first': now.isoformat()})
            if source not in entry['sources']:
                entry['sources'].append(source)
            if is_trusted:
                entry['trusted'] = True
        seen.add(b['pathname'])

    ready, stale = [], []
    for k, e in list(pending.items()):
        age = (now - dt.datetime.fromisoformat(e['first'])).days
        n = len(e['sources'])
        src = '공유(내 브라우저)' if e.get('trusted') else f'공유({n}명)'
        item = {'cat': e['cat'], 'q': e['q'], 'a': e['a'], 'note': e['note'], 'src': src}
        if n >= REMOTE_MIN_SOURCES or e.get('trusted'):
            ready.append(item)
            del pending[k]
        elif age >= REMOTE_WAIT_DAYS:
            stale.append(item)
            del pending[k]

    log(f'공유 서버: 새 제출 {len(new_blobs)}건 → 합칠 준비 {len(ready)}개, 1명만 보내 대기 중 {len(pending)}개'
        + (f', 지워서 취소 {retracted}개' if retracted else ''))
    if apply:
        state['remote_seen'] = sorted(seen)
        state['remote_pending'] = pending
    return ready, stale, pending


# ===== 판정 =====

class Bank:
    def __init__(self, name):
        self.name = name
        self.path = os.path.join(DATA_DIR, f'{name}.csv')
        self.rows = load_bank(self.path)
        self.by_q = {}
        for q, a in self.rows:
            self.by_q.setdefault(qkey(q), []).append(a)
        # 답 표기 사전: 정규화 키 → 대표 표기
        self.answers = {}
        for _, a in self.rows:
            k = akey(a)
            if len(k) >= 2 and k not in self.answers:
                self.answers[k] = answer_core(a)
        self.answers_by_len = {}
        for k in self.answers:
            self.answers_by_len.setdefault(len(k), []).append(k)
        # 답 → 그 답을 가진 질문들 (교정이 맞는지 확인용)
        self.questions_by_answer = {}
        for q, a in self.rows:
            self.questions_by_answer.setdefault(akey(a), []).append(qkey(q))

    def known_spelling(self, answer, question):
        """족보에 있는 표기로 교정. (교정된 답, 교정 여부)

        비슷한 철자의 다른 단어로 잘못 바꾸지 않도록(리소좀→리포좀), 족보에서 그 표기를 답으로 가진
        질문이 지금 질문과 비슷할 때만 고친다.
        """
        k = qkey(answer)
        if k in self.answers:
            return answer, False
        if len(k) < 3 or question.startswith('(가려진 문제)'):
            return answer, False
        best, best_score, second = None, 0, 0
        for n in (len(k) - 1, len(k), len(k) + 1):
            for cand in self.answers_by_len.get(n, []):
                s = jamo_ratio(k, cand)
                if s > best_score:
                    best, second, best_score = cand, best_score, s
                elif s > second:
                    second = s
        if best and best_score >= 85 and best_score - second >= 5:
            qk_now = qkey(question)
            related = any(difflib.SequenceMatcher(None, qk_now, other).ratio() >= 0.55
                          for other in self.questions_by_answer.get(best, []))
            if related:
                return self.answers[best], True
        return answer, False


def judge(item, bank, batch_keys):
    """('merge', 질문, 답, 메모) | ('review', 사유) | ('skip', 사유)"""
    q, a, cat = item['q'], item['a'], item['cat']

    if not q or not a:
        return ('skip', '빈 항목')
    # 게임 안내 화면("'국어ㆍ영어ㆍ과학' 에 관한 문제입니다.")이 질문으로 잡힌 것 (OCR이 한 글자씩 띄어 읽던 때의 수집 오류)
    if re.search(r'에관한문제입니다|사용자대기중|오신것을환영', re.sub(r'\s+', '', q)):
        return ('skip', '게임 안내 화면이 질문으로 잡힘')
    if cat != 'ox_xo' and a.strip().upper() in ('O', 'X'):
        return ('skip', 'OX 답이 다른 카테고리에 있음')

    core = answer_core(a)
    if len(core) > 40:
        return ('review', '답이 너무 김')
    if garbage_ratio(core) > 0:
        return ('review', '답에 깨진 글자')

    hidden = q.startswith('(가려진 문제)')
    if not hidden and garbage_ratio(q) >= 0.05:
        return ('review', '질문에 깨진 글자 (OCR 잡음)')
    if not hidden and OVERLAY_RE.search(q):
        return ('review', '출제자 이름이 질문에 섞임 (OCR 잡음)')
    if not hidden and SANDWICH_RE.search(q):
        return ('review', '한글 사이에 영문 조각 (OCR 잡음)')
    if re.search(r'[|©®¦]', q) or re.search(r'[|©®¦]', a):
        return ('review', 'OCR 잡음 기호(| © 등)')
    if not hidden and len(qkey(q)) < 5:
        return ('review', '질문이 너무 짧음')

    fixed, corrected = bank.known_spelling(core, q)
    k_q, k_a = qkey(q), qkey(fixed)

    existing = bank.by_q.get(k_q, [])
    if any(akey(e) == k_a for e in existing):
        return ('skip', '이미 족보에 있음')
    if existing:
        return ('review', f'같은 질문의 족보 답이 다름: {existing[0]}')
    if (k_q, k_a) in batch_keys:
        return ('skip', '이번 묶음에서 중복')
    if len(qkey(fixed)) <= 2 and qkey(fixed) not in bank.answers and cat != 'ox_xo':
        return ('review', '두 글자 이하 새 답 (OCR 오인식 확인 필요)')

    note = f'자동교정 {core}→{fixed}' if corrected else ''
    return ('merge', q, fixed, note)


# ===== 반영 =====

def append_rows(path, rows):
    raw = open(path, 'rb').read() if os.path.exists(path) else b'Question\tAnswer\r\n'
    nl = b'\r\n' if b'\r\n' in raw else b'\n'
    if not raw.endswith(nl):
        raw += nl
    raw += b''.join(f'{q}\t{a}{COLLECT_TAG}'.encode('utf-8') + nl for q, a in rows)
    with open(path, 'wb') as f:
        f.write(raw)


def sync_web():
    for name in CATEGORIES:
        shutil.copyfile(os.path.join(DATA_DIR, f'{name}.csv'), os.path.join(WEB_CATEGORIES, f'{name}.csv'))
    with open(WEB_METADATA, encoding='utf-8') as f:
        meta = json.load(f)
    total = 0
    for c in meta['categories']:
        c['count'] = len(load_bank(os.path.join(WEB_REPO, c['file'])))
        total += c['count']
    meta['total_items'] = total
    meta['generated_at'] = dt.date.today().isoformat()
    with open(WEB_METADATA, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(meta, f, ensure_ascii=False, indent=2)
    return total


def git(repo, *args, check=True):
    r = subprocess.run(['git', '-C', repo, *args], capture_output=True, text=True, encoding='utf-8')
    if check and r.returncode != 0:
        raise RuntimeError(f'git {" ".join(args)} 실패 ({repo}): {r.stderr.strip()}')
    return r.stdout.strip()


def ensure_clean_and_updated(repo, branch):
    if git(repo, 'status', '--porcelain', '--untracked-files=no'):
        raise RuntimeError(f'{repo} 에 커밋 안 된 변경이 있어 건너뜁니다. 정리 후 다시 실행하세요.')
    current = git(repo, 'branch', '--show-current')
    if current != branch:
        raise RuntimeError(f'{repo} 가 {branch} 브랜치가 아닙니다 (현재 {current}).')
    git(repo, 'pull', '--ff-only', '-q')


def commit_and_push(repo, paths, message, push):
    git(repo, 'add', *paths)
    if not git(repo, 'diff', '--cached', '--name-only'):
        return False
    git(repo, 'commit', '-q', '-m', message)
    if push:
        git(repo, 'push', '-q')
    return True


def _state_url():
    import urllib.parse
    return f"{REMOTE_URL}?{urllib.parse.urlencode({'state': '1', 'key': admin_key()})}"


def load_remote_state():
    """서버에 둔 공유 상태. (상태 dict 또는 None, 읽기 성공 여부)"""
    if not admin_key():
        return None, False
    status, body = http_json(_state_url())
    if status != 200 or not body.get('ok'):
        return None, False
    return body.get('state'), True


def save_remote_state(data):
    import urllib.request
    import urllib.error
    if not admin_key():
        return False
    req = urllib.request.Request(_state_url(), method='PUT',
                                 data=json.dumps({'state': data}, ensure_ascii=False).encode('utf-8'),
                                 headers={'Content-Type': 'application/json', 'User-Agent': 'qplay-merge-routine'})
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            return r.status == 200
    except (urllib.error.URLError, OSError):
        return False


def load_state(apply=False):
    """PC와 클라우드가 같은 상태를 보도록 서버 상태를 먼저 쓰고, 없으면 PC 파일을 쓴다."""
    local = None
    try:
        with open(STATE_PATH, encoding='utf-8') as f:
            local = json.load(f)
    except (OSError, ValueError):
        pass
    remote, ok = load_remote_state()
    if remote is not None:
        state = remote
    elif IN_CI and apply:
        raise RuntimeError('서버의 병합 상태가 없거나 읽지 못해 중단합니다 (관리자 키·네트워크 확인).')
    else:
        if apply and not ok:
            log('공유 서버 상태를 못 읽어 PC 상태 파일로 진행합니다')
        state = local or {}
    state['processed'] = set(state.get('processed', []))
    return state


def save_state(state):
    data = dict(state, processed=sorted(state['processed']))
    if not IN_CI:
        os.makedirs(WORK_DIR, exist_ok=True)
        with open(STATE_PATH, 'w', encoding='utf-8') as f:
            json.dump(data, f, ensure_ascii=False, indent=0)
    if not save_remote_state(data):
        if IN_CI:
            raise RuntimeError('서버에 병합 상태를 저장하지 못했습니다.')
        log('공유 서버에 상태를 저장하지 못했습니다 (다음 실행 때 다시 올림)')


def append_reviews(reviews):
    new_file = not os.path.exists(REVIEW_PATH)
    os.makedirs(WORK_DIR, exist_ok=True)
    with open(REVIEW_PATH, 'a', encoding='utf-8-sig' if new_file else 'utf-8') as f:
        if new_file:
            f.write('카테고리\t질문\t답\t사유\t출처\t메모\n')
        for it, reason in reviews:
            f.write(f"{it['cat']}\t{it['q']}\t{it['a']}\t{reason}\t{it['src']}\t{it.get('note', '')}\n")


def drain_review_queue(state):
    """클라우드 실행이 쌓아 둔 검토 항목을 PC의 검토필요.tsv로 옮긴다."""
    queue = state.get('review_queue') or []
    if IN_CI or not queue:
        return 0
    append_reviews([(q, q['reason']) for q in queue])
    state['review_queue'] = []
    return len(queue)


# ===== 수집 현황 페이지 =====

def _esc(text):
    import html
    return html.escape(str(text if text is not None else ''))


def _table(headers, rows, empty):
    if not rows:
        return f'<p class="empty">{_esc(empty)}</p>'
    head = ''.join(f'<th>{_esc(h)}</th>' for h in headers)
    body = ''.join('<tr>' + ''.join(f'<td>{_esc(c)}</td>' for c in r) + '</tr>' for r in rows)
    return f'<div class="wrap"><table><thead><tr>{head}</tr></thead><tbody>{body}</tbody></table></div>'


def read_review_file():
    rows = []
    if os.path.exists(REVIEW_PATH):
        with open(REVIEW_PATH, encoding='utf-8-sig') as f:
            for i, line in enumerate(f.read().split('\n')):
                parts = line.rstrip('\r').split('\t')
                if i == 0 or len(parts) < 4:
                    continue
                rows.append(parts + [''] * (6 - len(parts)))
    return rows


def write_report(state, pending, desktop_items, planned, preview, planned_reviews=()):
    """문서\\QuizHelper 수집\\수집현황.html 을 새로 쓴다."""
    label = lambda c: CATEGORY_LABELS.get(c, c)
    history = list(reversed(state.get('merged_history', [])))
    review = list(reversed(read_review_file()))
    waiting = sorted(pending.values(), key=lambda e: e.get('first', ''), reverse=True)
    now = dt.datetime.now()

    def days_ago(iso):
        try:
            d = dt.datetime.fromisoformat(iso)
            return f'{(dt.datetime.now(dt.timezone.utc) - d).days}일 전'
        except (TypeError, ValueError):
            return ''

    sections = []
    if preview and planned:
        sections.append(('🟡 이번에 합칠 예정 (미리보기라 아직 안 합침)', len(planned), _table(
            ['카테고리', '질문', '답', '출처'],
            [(label(e['cat']), e['q'], e['a'], e['src']) for e in planned], '')))
    if preview and planned_reviews:
        sections.append(('🟠 이번에 검토로 보낼 예정 (미리보기)', len(planned_reviews), _table(
            ['카테고리', '질문', '답', '사유', '출처'],
            [(label(it['cat']), it['q'], it['a'], reason, it['src']) for it, reason in planned_reviews], '')))
    sections.append(('✅ 족보에 합쳐진 문제', len(history), _table(
        ['합친 날', '카테고리', '질문', '답', '출처', '메모'],
        [(e.get('date', ''), label(e.get('cat')), e.get('q'), e.get('a'), e.get('src'), e.get('note', '')) for e in history],
        '아직 합쳐진 문제가 없어요.')))
    sections.append(('⏳ 다른 사용자 확인 대기 (2명이 같은 답을 보내면 합쳐짐)', len(waiting), _table(
        ['처음 받은 때', '카테고리', '질문', '답', '보낸 사람'],
        [(days_ago(e.get('first')), label(e.get('cat')), e.get('q'), e.get('a'), f"{len(e.get('sources', []))}명") for e in waiting],
        f'기다리는 문제가 없어요. ({REMOTE_WAIT_DAYS}일 동안 아무도 확인 안 하면 검토로 넘어가요)')))
    sections.append(('🔍 검토 필요 (자동으로 넣지 않은 것)', len(review), _table(
        ['카테고리', '질문', '답', '사유', '출처', '메모'],
        [(label(r[0]), r[1], r[2], r[3], r[4], r[5]) for r in review],
        '검토할 문제가 없어요.')))
    sections.append(('💻 이 PC 설치형 앱이 모은 문제 (원본)', len(desktop_items), _table(
        ['카테고리', '질문', '답', '메모'],
        [(label(it['cat']), it['q'], it['a'], it['note']) for it in reversed(desktop_items)],
        '이 PC 앱이 모은 문제가 아직 없어요.')))

    cards = ''.join(f'<div class="card"><div class="n">{n:,}</div><div class="t">{_esc(t.split(" (")[0])}</div></div>'
                    for t, n, _ in sections)
    body = ''.join(f'<section><h2>{_esc(t)} <span class="count">{n:,}</span></h2>{tbl}</section>' for t, n, tbl in sections)
    page = f"""<!doctype html>
<html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>수집 현황</title>
<style>
:root {{ --bg:#f7f7f5; --fg:#1d1d1f; --muted:#6b6b70; --card:#fff; --line:#e4e4e0; --accent:#2f6f4f; }}
@media (prefers-color-scheme: dark) {{ :root {{ --bg:#17181a; --fg:#ececec; --muted:#9a9aa0; --card:#212226; --line:#33343a; --accent:#7bc59b; }} }}
* {{ box-sizing:border-box; }}
body {{ margin:0; padding:24px 16px 48px; background:var(--bg); color:var(--fg);
  font:15px/1.5 "Malgun Gothic","Apple SD Gothic Neo",system-ui,sans-serif; }}
main {{ max-width:1100px; margin:0 auto; }}
h1 {{ font-size:22px; margin:0 0 4px; }}
.sub {{ color:var(--muted); margin:0 0 20px; font-size:13px; }}
.cards {{ display:grid; grid-template-columns:repeat(auto-fit,minmax(150px,1fr)); gap:10px; margin-bottom:20px; }}
.card {{ background:var(--card); border:1px solid var(--line); border-radius:10px; padding:12px 14px; }}
.card .n {{ font-size:24px; font-weight:700; color:var(--accent); }}
.card .t {{ font-size:12px; color:var(--muted); }}
input {{ width:100%; padding:10px 12px; border:1px solid var(--line); border-radius:8px; background:var(--card);
  color:var(--fg); font:inherit; margin-bottom:8px; }}
section {{ margin-top:26px; }}
h2 {{ font-size:16px; margin:0 0 8px; }}
.count {{ color:var(--muted); font-weight:400; font-size:13px; }}
.wrap {{ overflow-x:auto; border:1px solid var(--line); border-radius:10px; background:var(--card); }}
table {{ border-collapse:collapse; width:100%; font-size:13.5px; }}
th, td {{ text-align:left; padding:8px 10px; border-bottom:1px solid var(--line); vertical-align:top; }}
th {{ color:var(--muted); font-weight:600; white-space:nowrap; position:sticky; top:0; background:var(--card); }}
tr:last-child td {{ border-bottom:0; }}
.empty {{ color:var(--muted); background:var(--card); border:1px dashed var(--line); border-radius:10px; padding:14px; margin:0; }}
</style></head>
<body><main>
<h1>📥 그깽족보 수집 현황</h1>
<p class="sub">{now:%Y-%m-%d %H:%M} 기준{' · 미리보기 (족보는 바꾸지 않음)' if preview else ''} · 최신으로 보려면 같은 폴더의 「수집현황 새로고침」을 더블클릭하세요.</p>
<div class="cards">{cards}</div>
<input id="q" type="search" placeholder="질문이나 답으로 찾기..." oninput="filter(this.value)">
{body}
</main>
<script>
function filter(v) {{
  v = v.trim().toLowerCase();
  document.querySelectorAll('tbody tr').forEach(function (tr) {{
    tr.style.display = !v || tr.textContent.toLowerCase().indexOf(v) >= 0 ? '' : 'none';
  }});
}}
</script>
</body></html>
"""
    os.makedirs(WORK_DIR, exist_ok=True)
    with open(REPORT_PATH, 'w', encoding='utf-8') as f:
        f.write(page)
    ensure_refresh_shortcut()


WEB_STATUS_NAME = 'collected-status.json'
FILE_TO_WEB_SHEET = {v: k for k, v in WEB_SHEET_TO_FILE.items()}


def write_web_status(state, pending, review_count):
    """웹 사이트의 '모은 문제' 페이지가 읽는 공개 현황 파일. 내용이 바뀌었으면 True.

    족보에 이미 들어간 문제와 숫자만 담는다 (확인 전·검토 중인 문제 내용은 공개하지 않음).
    """
    history = state.get('merged_history', [])
    data = {
        'merged_total': len(history),
        'last_merge': history[-1]['date'] if history else None,
        'pending_count': len(pending),
        'review_count': review_count,
        'merged': [{'date': e.get('date'), 'sheet': FILE_TO_WEB_SHEET.get(e.get('cat'), e.get('cat')),
                    'q': e.get('q'), 'a': e.get('a')} for e in reversed(history[-500:])],
    }
    text = json.dumps(data, ensure_ascii=False, indent=1) + '\n'
    path = os.path.join(WEB_REPO, WEB_STATUS_NAME)
    try:
        with open(path, encoding='utf-8') as f:
            if f.read() == text:
                return False
    except OSError:
        pass
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)
    return True


def ensure_refresh_shortcut():
    """더블클릭하면 최신 현황을 받아 페이지를 여는 파일 (족보는 바꾸지 않는 미리보기 실행)."""
    if os.path.exists(REFRESH_CMD_PATH):
        return
    python = sys.executable
    store_alias = os.path.join(os.environ.get('LOCALAPPDATA', ''), 'Microsoft', 'WindowsApps', 'python.exe')
    if 'WindowsApps' in python and os.path.exists(store_alias):
        python = store_alias
    script = os.path.abspath(__file__)
    lines = [
        '@echo off',
        'chcp 65001 >nul',
        'echo 최신 수집 현황을 가져오는 중...',
        f'"{python}" -I "{script}" --report-only >nul 2>&1',
        f'start "" "{REPORT_PATH}"',
    ]
    with open(REFRESH_CMD_PATH, 'w', encoding='utf-8', newline='\r\n') as f:
        f.write('\n'.join(lines) + '\n')


def main():
    ap = argparse.ArgumentParser(description='수집 문제를 족보에 자동 병합')
    ap.add_argument('--apply', action='store_true', help='족보 파일에 반영')
    ap.add_argument('--push', action='store_true', help='반영 후 두 저장소 커밋·푸시 (--apply 포함)')
    ap.add_argument('--notify-always', action='store_true', help='합칠 게 없어도 끝났다고 알림')
    ap.add_argument('--report-only', action='store_true', help='수집 현황 페이지만 새로 만듦 (미리보기와 같음)')
    args = ap.parse_args()
    apply = (args.apply or args.push) and not args.report_only

    log(f'===== 수집 문제 병합 {dt.datetime.now():%Y-%m-%d %H:%M} ({"푸시" if args.push else "반영" if apply else "미리보기"}) =====')

    if apply:
        ensure_clean_and_updated(DESKTOP_REPO, 'main')
        ensure_clean_and_updated(WEB_REPO, 'master')

    state = load_state(apply)
    processed = state['processed']
    desktop_items = read_desktop_collected()
    web_items, web_files = read_web_exports()
    remote_ready, remote_stale, remote_pending = fetch_remote(state, apply)
    items = desktop_items + web_items + remote_ready
    for it in items + remote_stale:
        it['q'] = tidy_text(it['q'], question=True)
        it['a'] = tidy_text(it['a'])
    fresh = [it for it in items if f"{it['cat']}|{qkey(it['q'])}|{akey(it['a'])}" not in processed]
    log(f'수집 항목: 설치형 {len(desktop_items)}개, 웹 파일 {len(web_files)}개({len(web_items)}개), 공유 {len(remote_ready)}개 → 새 항목 {len(fresh)}개')

    banks = {name: Bank(name) for name in CATEGORIES}
    to_merge = {name: [] for name in CATEGORIES}
    reviews, batch_keys, done_keys = [], set(), set()
    merged_entries = []
    today = dt.date.today().isoformat()

    for it in fresh:
        state_key = f"{it['cat']}|{qkey(it['q'])}|{akey(it['a'])}"
        verdict = judge(it, banks[it['cat']], batch_keys)
        if verdict[0] == 'merge':
            _, q, a, note = verdict
            to_merge[it['cat']].append((q, a))
            merged_entries.append({'date': today, 'cat': it['cat'], 'q': q, 'a': a, 'src': it['src'], 'note': note})
            batch_keys.add((qkey(q), qkey(a)))
            log(f"  + [{it['cat']}] {q[:50]} → {a}" + (f'  ({note})' if note else ''))
            done_keys.add(state_key)
        elif verdict[0] == 'review':
            reviews.append((it, verdict[1]))
            log(f"  ? [{it['cat']}] {it['q'][:50]} → {it['a']}  ({verdict[1]})")
            done_keys.add(state_key)  # 검토 파일로 넘겼으니 다시 처리하지 않음
        else:
            done_keys.add(state_key)

    # 1명만 보낸 채 오래 기다린 공유 항목은 검토로
    for it in remote_stale:
        state_key = f"{it['cat']}|{qkey(it['q'])}|{akey(it['a'])}"
        if state_key in processed:
            continue
        reviews.append((it, f'1명만 보냄 ({REMOTE_WAIT_DAYS}일 동안 확인 안 됨)'))
        log(f"  ? [{it['cat']}] {it['q'][:50]} → {it['a']}  (1명만 보냄)")
        done_keys.add(state_key)

    merged_total = sum(len(v) for v in to_merge.values())
    log(f'합칠 항목 {merged_total}개, 검토 필요 {len(reviews)}개')

    if not apply:
        log('미리보기라서 파일은 바꾸지 않았습니다. 반영하려면 --apply 또는 --push')
        write_report(state, remote_pending, desktop_items, merged_entries, preview=True, planned_reviews=reviews)
        return 0

    if merged_entries:
        state['merged_history'] = (state.get('merged_history', []) + merged_entries)[-5000:]

    if reviews and IN_CI:
        state['review_queue'] = (state.get('review_queue') or []) + [
            {'cat': it['cat'], 'q': it['q'], 'a': it['a'], 'src': it['src'], 'note': it.get('note', ''), 'reason': reason}
            for it, reason in reviews]
        log(f'검토 필요 {len(reviews)}개는 다음 PC 루틴 때 검토필요.tsv로 옮겨집니다.')
    elif reviews:
        append_reviews(reviews)
        log(f'검토 필요 항목은 {REVIEW_PATH} 에 추가했습니다.')
    drained = drain_review_queue(state)
    if drained:
        log(f'클라우드 실행이 남긴 검토 항목 {drained}개를 검토필요.tsv로 옮겼습니다.')

    # 웹 '모은 문제' 페이지가 읽는 공개 현황 (족보에 들어간 문제 + 숫자만)
    review_count = len(read_review_file()) + len(state.get('review_queue') or [])
    status_changed = write_web_status(state, remote_pending, review_count)

    if merged_total:
        for name, rows in to_merge.items():
            if rows:
                append_rows(banks[name].path, rows)
        total = sync_web()
        log(f'웹 족보 동기화 완료: 총 {total:,}문제')

        summary = ', '.join(f'{n} +{len(r)}' for n, r in to_merge.items() if r)
        message = f'data: 수집 문제 {merged_total}개 자동 병합 ({summary})'
        d = commit_and_push(DESKTOP_REPO, ['QuizHelper/data'], message, args.push)
        w = commit_and_push(WEB_REPO, ['categories', 'metadata.json', WEB_STATUS_NAME], message, args.push)
        log(f'커밋: 설치형 {"O" if d else "-"}, 웹 {"O" if w else "-"}' + (' (푸시 완료, 웹 사이트 곧 반영)' if args.push else ' (푸시 안 함)'))
    elif status_changed:
        commit_and_push(WEB_REPO, [WEB_STATUS_NAME], 'data: 수집 현황 갱신', args.push)
        log('웹 수집 현황만 갱신')

    processed |= done_keys
    save_state(state)
    write_report(state, remote_pending, desktop_items, [], preview=False)

    # 처리한 웹 내보내기 파일은 imported 폴더로 옮김
    if web_files:
        os.makedirs(IMPORTED_DIR, exist_ok=True)
        for path in web_files:
            shutil.move(path, os.path.join(IMPORTED_DIR, os.path.basename(path)))

    # 끝났다는 알림 (새 항목이 있었을 때, 또는 --notify-always)
    if merged_total or reviews:
        lines = []
        if merged_total:
            added = ', '.join(f'{CATEGORY_LABELS[n]} +{len(r)}' for n, r in to_merge.items() if r)
            where = '웹 사이트에 반영됨' if args.push else '아직 GitHub에 안 올림'
            lines.append(f'{merged_total}문제 추가 ({added}) · {where}')
        if reviews:
            lines.append(f'검토 필요 {len(reviews)}개 → 검토필요.tsv')
        if remote_pending:
            lines.append(f'다른 사용자 확인 대기 {len(remote_pending)}개')
        notify('📥 족보 업데이트 완료', '\n'.join(lines))
    elif args.notify_always:
        notify('📥 족보 병합 루틴', '새로 합칠 문제가 없었어요')

    return 0


if __name__ == '__main__':
    try:
        code = main()
    except Exception as e:  # 예약 실행 중 실패도 로그에 남기고 알린다
        log(f'[실패] {e}')
        notify('⚠️ 족보 병합 루틴 실패', str(e)[:200])
        code = 1
    finally:
        try:
            os.makedirs(WORK_DIR, exist_ok=True)
            with open(LOG_PATH, 'a', encoding='utf-8') as f:
                f.write('\n'.join(_log_lines) + '\n\n')
        except OSError:
            pass
    sys.exit(code)
