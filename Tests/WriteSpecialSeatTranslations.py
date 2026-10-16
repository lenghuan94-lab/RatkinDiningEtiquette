"""Authoring source for 1.2.0 seat texts; updates only the new XML entries."""
from pathlib import Path
from xml.sax.saxutils import escape
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
TEXTS = {
    'English': {
        'Toilet': [
            ('not my dinner chair', 'They put me on the potty with my dinner. Cold bottom, funny smell... I want a hug.'),
            ('potty dinner again', 'Dinner here again? I tuck my tail up and hold my food tight. Please take me somewhere else.'),
            ('take me off, please', 'I reached up for a hug and got another potty dinner. I ate, but I still want to be picked up.'),
            ('the wrong kind of bowl', 'I know what this is for. Calling it my little dinner table does not make it one.'),
            ('the bathroom regular', 'Back at the toilet for dinner. I ate quickly and hoped nobody would come in.'),
            ('my very own unwanted diner', 'Everyone has a usual dinner spot now. Mine has a flush. Please stop laughing.'),
            ('do not call it a throne', 'Dinner on a toilet. A royal banquet, apparently. My ears are burning, and I am not laughing.'),
            ('the joke is still not funny', 'Another meal on this seat. I kept my tail clear and my eyes on the door.'),
            ('do not tell anyone', 'Yes, this is where I eat now. No, you may not give my toilet diner a name.'),
            ('dignity served on porcelain', 'There is a meal, a toilet, and me trying to pretend those things belong together.'),
            ('a reservation nobody wanted', 'The toilet again. Apparently my dinner reservation includes plumbing and an audience.'),
            ('the house special is resignation', 'Meal after meal here. I know where to put my tail now. That is not a skill I wanted.')],
        'SpecialSeat': [
            ('a strange dinner perch', 'They put me on a strange seat with my food. I curled my tail up. Can we eat somewhere familiar?'),
            ('the strange seat again', 'This seat again... I hold my dinner close and look for the person who put me here.'),
            ('my paws want a hug', 'Another dinner up here. My tummy is full, but my paws still reach for someone to take me down.'),
            ('this is not a dinner table', 'They said any seat would do. This one looks as if it has a very different job.'),
            ('borrowed seat, uneasy dinner', 'Eating here again. I keep wondering whether somebody is about to need this thing.'),
            ('the colony odd-seat diner', 'My usual dinner spot is now a seat with another purpose. Nobody asked whether I wanted that.'),
            ('please stop watching', 'Dinner on this peculiar seat. I can hear the jokes already. Let me finish in peace.'),
            ('the seat gets another shift', 'Another meal here. This seat has its real job, then an extra shift humiliating me.'),
            ('this joke has become furniture', 'I have eaten here so often that people point me straight to it. My ears flatten every time.'),
            ('a seat doing the wrong job', 'A meal on furniture meant for something else. A practical solution, if one ignores my dignity.'),
            ('the makeshift spot becomes routine', 'Back here for dinner. The arrangement gets less temporary while my objections get quieter.'),
            ('assigned to the odd seat', 'This is my dinner spot now, apparently. I finish the meal and leave before anyone makes another remark.')],
        'Settings': ['Toilets and other special seats', 'Allow these seats as lone-chair dining spots by eater identity, including toddlers. Defaults: prisoners and slaves on; colonists and visitors off. Normal chairs are preferred. Toddler meals also require the corresponding experimental help setting; its existing defaults are unchanged.', 'Colonist Ratkin: special-seat dining', 'Visitor Ratkin: special-seat dining', 'Prisoner Ratkin: special-seat dining', 'Slave Ratkin: special-seat dining']},
    'ChineseSimplified': {
        'Toilet': [
            ('这不是饭饭椅', '把我放在便便椅上吃饭饭。屁股凉凉，味道怪怪……想要抱抱。'),
            ('又是便便椅饭饭', '又在这里吃呀？尾巴收好，饭饭抱紧。可以换个地方吗？'),
            ('吃完也要抱走', '伸爪爪要抱抱，结果又被放在便便椅上。饭饭吃完了，还是想被抱走。'),
            ('这个碗不能装饭', '我知道这个是干什么的！叫它“鼠鼠小餐桌”，也不会真的变成餐桌。'),
            ('厕所里的常客', '又来马桶边吃饭了。我吃得很快，希望这时候没人推门进来。'),
            ('专属餐厅带冲水', '大家都有固定饭点了。我的饭点能冲水……不要再笑啦。'),
            ('别叫它鼠王宝座', '在马桶上吃饭，还说这是鼠王的御膳。耳朵都红了，我可没有觉得好笑。'),
            ('这个笑话不好笑', '又是这张座位。我把尾巴避开，边吃边盯着门，生怕有人进来。'),
            ('不许给餐厅起名', '对，我现在就在这里吃饭。对，不许给我的马桶餐厅起外号。'),
            ('瓷器盛不住体面', '一份饭，一个马桶，一只努力装作这三样很搭的鼠。'),
            ('谁订的卫浴雅座', '又是马桶席。原来我的用餐预约还包含排水系统和围观服务。'),
            ('本店招牌叫认命', '一餐又一餐。现在我连尾巴该放哪儿都熟练了，可我没想学这个。')],
        'SpecialSeat': [
            ('饭饭椅好奇怪', '把我放到奇怪的座位上吃饭饭。尾巴卷好……能去熟悉的地方吗？'),
            ('又坐怪怪椅', '又是这张椅子……抱紧饭饭，找找把我放上来的人。'),
            ('爪爪还想要抱', '又在这里吃。肚肚饱了，爪爪还是伸着，想让人抱我下去。'),
            ('它明明有别的用处', '说是能坐就能吃，可这东西明明是拿来做别的事情的。'),
            ('借座位也借不来安心', '又在这里吃饭。我总觉得下一刻就会有人来用这个东西。'),
            ('殖民地奇座餐馆', '我的固定饭点居然是个另有用途的座位，也没人问过我愿不愿意。'),
            ('别盯着我吃了', '在这种座位上吃饭，已经能想到他们会说什么了。让我安静吃完吧。'),
            ('座位兼职我出丑', '又一餐。这东西先做本职工作，然后加一班让我丢脸。'),
            ('笑话都坐成固定席了', '吃了太多次，大家已经直接指这里给我。每次耳朵都会垂下来。'),
            ('座位被安排了副业', '在另有用途的家具上吃饭。办法倒是实用，前提是别算上我的体面。'),
            ('临时饭点长期续约', '又回这里吃饭。安排越来越固定，反对的声音却越来越小。'),
            ('奇怪座位指定用餐', '看来这里就是我的饭点了。吃完赶紧走，免得又听到一句调侃。')],
        'Settings': ['马桶及其他特殊座位进食', '按吃饭鼠族的身份决定能否把这些座位当独立椅子使用，同样适用于鼠蛋。默认仅囚犯、奴隶开启，殖民者、访客关闭；优先选择普通椅子。鼠蛋还需开启对应身份的实验性求抱设置，该设置原有默认值不变。', '殖民者鼠族：允许特殊座位进食', '访客鼠族：允许特殊座位进食', '囚犯鼠族：允许特殊座位进食', '奴隶鼠族：允许特殊座位进食']},
    'Japanese': {
        'Toilet': [
            ('ごはんの椅子じゃない', 'トイレの椅子に乗せられてごはん。おしりが冷たい、変なにおい……抱っこして。'),
            ('またトイレでごはん', 'またここ？ しっぽを丸めて、ごはんをぎゅっ。ほかの場所がいいな。'),
            ('食べたら抱っこして', '抱っこしてと手を伸ばしたのに、またトイレでごはん。食べ終わっても抱っこしてほしい。'),
            ('これは食器じゃない', 'これが何に使うものか知ってるよ。「小さな食卓」って呼んでも食卓にはならない。'),
            ('トイレの常連さん', 'またトイレで食事。急いで食べた。誰も扉を開けませんように。'),
            ('専用食堂は水洗式', 'みんなに決まった食事場所ができた。私の場所は水が流れる……もう笑わないで。'),
            ('玉座って呼ばないで', 'トイレで食事。王様の晩餐だって。耳まで赤くなったけど、全然笑えない。'),
            ('その冗談は面白くない', 'またこの席。しっぽを避けて、誰か来ないか扉を見ながら食べた。'),
            ('食堂に名前をつけるな', 'そう、今はここで食べてる。だからってトイレ食堂にあだ名をつけないで。'),
            ('陶器には載らない尊厳', '食事とトイレと私。三つが似合っているふりをするのに必死だ。'),
            ('誰がこの席を予約した', 'またトイレ席。食事の予約に配管と見物客までついてくるらしい。'),
            ('名物はあきらめ', '何度もここで食べた。しっぽの置き場まで覚えた。覚えたくなかったのに。')],
        'SpecialSeat': [
            ('へんなごはんの席', 'へんな席に乗せられてごはん。しっぽをくるん。いつもの場所に行きたいな。'),
            ('またへんな椅子', 'またこの椅子……ごはんを抱えて、乗せてくれた人を探す。'),
            ('まだ抱っこしてほしい', 'またここでごはん。おなかはいっぱい。でも降ろしてほしくて手を伸ばす。'),
            ('別の仕事があるはず', '座れるなら食べられるって。でもこれは別のことに使う物でしょ。'),
            ('借りた席では落ち着かない', 'またここで食事。誰かがこれを使いに来そうで落ち着かない。'),
            ('コロニーの変な席食堂', '別の用途がある席が私の定位置になった。私がそれを望むか、誰も聞かなかった。'),
            ('食べるところを見ないで', 'こんな席で食事。もう冗談が聞こえてきそう。静かに食べさせて。'),
            ('席の副業は私の赤っ恥', 'また一食。この席は本来の仕事をして、追加で私を恥ずかしがらせる。'),
            ('冗談が指定席になった', '何度も食べたせいで、みんな迷わずここを指す。そのたびに耳が垂れる。'),
            ('別の仕事をさせられた席', '別の用途の家具で食事。私の体面を計算に入れなければ、実用的な解決策だ。'),
            ('仮の食事場所が日常に', 'またここで食事。配置は固定され、抗議の声は小さくなるばかり。'),
            ('変な席に食事を指定', 'ここが私の食事場所らしい。次のからかいが来る前に食べ終えて立ち去ろう。')],
        'Settings': ['トイレなどの特殊な席での食事', '食べるラットキンの身分ごとに、特殊な席を独立した椅子として使えるか設定します。幼児にも適用。初期設定は囚人と奴隷のみ有効、入植者と訪問者は無効。通常の椅子を優先します。幼児には対応する実験的な抱っこ補助設定も必要で、その初期設定は従来どおりです。', '入植者ラットキン：特殊な席で食事', '訪問者ラットキン：特殊な席で食事', '囚人ラットキン：特殊な席で食事', '奴隷ラットキン：特殊な席で食事']},
    'Korean': {
        'Toilet': [
            ('밥 먹는 의자가 아니야', '변기 의자에 올려놓고 밥을 줬어. 엉덩이가 차갑고 냄새도 이상해… 안아 줘.'),
            ('또 변기에서 밥', '또 여기야? 꼬리를 말고 밥을 꼭 안았어. 다른 데로 가면 안 돼?'),
            ('다 먹어도 안아 줘', '안아 달라고 손을 뻗었는데 또 변기 밥이야. 다 먹었지만 아직 안아 줬으면 좋겠어.'),
            ('밥 담는 그릇이 아니야', '이게 어디에 쓰는 건지 알아! 작은 식탁이라고 부른다고 식탁이 되지는 않아.'),
            ('화장실 단골손님', '또 변기에서 밥을 먹었어. 빨리 먹으면서 아무도 문을 열지 않길 바랐어.'),
            ('전용 식당은 물내림식', '다들 정해진 식사 자리가 생겼어. 내 자리는 물도 내려가… 이제 웃지 마.'),
            ('왕좌라고 부르지 마', '변기에서 밥을 먹는데 왕의 만찬이래. 귀가 빨개졌지만 하나도 안 웃겨.'),
            ('그 농담 재미없어', '또 이 자리야. 꼬리를 피하고 누가 들어올까 봐 문을 보면서 먹었어.'),
            ('식당 이름 짓지 마', '그래, 이제 여기서 밥을 먹어. 그렇다고 내 변기 식당에 별명을 붙여도 되는 건 아니야.'),
            ('도자기에 못 담는 체면', '밥과 변기와 나. 이 셋이 어울리는 척하느라 애쓰고 있다.'),
            ('누가 이 자리를 예약했나', '또 변기 자리다. 내 식사 예약에는 배수 시설과 구경꾼까지 포함되나 보다.'),
            ('오늘의 특선은 체념', '여기서 몇 끼째인지. 이제 꼬리를 어디에 둘지도 익숙하다. 배우고 싶었던 건 아닌데.')],
        'SpecialSeat': [
            ('이상한 밥 자리', '이상한 자리에 올려놓고 밥을 줬어. 꼬리를 돌돌. 익숙한 데로 가고 싶어.'),
            ('또 이상한 의자', '또 이 의자야… 밥을 꼭 안고 나를 올려 준 사람을 찾아.'),
            ('아직 안아 줬으면 해', '또 여기서 밥을 먹어. 배는 부른데 내려 달라고 손은 계속 뻗고 있어.'),
            ('다른 용도가 있잖아', '앉을 수 있으면 먹을 수도 있대. 하지만 이건 다른 일에 쓰는 물건이잖아.'),
            ('빌린 자리의 불안한 식사', '또 여기서 식사한다. 누가 이 물건을 쓰러 올 것 같아서 불안하다.'),
            ('정착지의 이상한 자리 식당', '다른 용도의 자리가 내 식사 자리로 굳었다. 내가 원하는지는 아무도 안 물었다.'),
            ('먹는 것 좀 보지 마', '이런 자리에서 식사하다니. 벌써 농담이 들리는 것 같다. 조용히 먹게 해 줘.'),
            ('자리의 부업은 망신 주기', '또 한 끼다. 이 자리는 본래 일을 하고 나를 창피하게 하는 추가 근무까지 한다.'),
            ('농담이 지정석이 됐다', '너무 자주 먹어서 다들 곧장 이곳을 가리킨다. 그때마다 귀가 축 처진다.'),
            ('다른 일을 맡은 자리', '다른 용도의 가구에서 식사했다. 내 체면을 빼면 실용적인 해결책이다.'),
            ('임시 식사 자리가 일상으로', '또 여기서 식사한다. 배치는 점점 고정되고 반대하는 목소리는 점점 작아진다.'),
            ('이상한 자리에서 지정 식사', '이곳이 내 식사 자리란다. 또 한마디 듣기 전에 식사를 끝내고 떠나자.')],
        'Settings': ['변기 및 기타 특수 자리에서 식사', '먹는 랫킨의 신분에 따라 특수 자리를 독립 의자처럼 쓸 수 있는지 설정합니다. 유아에게도 적용됩니다. 기본값은 죄수와 노예만 켜짐, 정착민과 방문객은 꺼짐입니다. 일반 의자를 우선합니다. 유아는 해당 신분의 실험적 안아 주기 설정도 필요하며, 그 기본값은 기존과 같습니다.', '정착민 랫킨: 특수 자리 식사 허용', '방문객 랫킨: 특수 자리 식사 허용', '죄수 랫킨: 특수 자리 식사 허용', '노예 랫킨: 특수 자리 식사 허용']},
    'Russian': {
        'Toilet': [
            ('это не стульчик для еды', 'Меня посадили на горшок с едой. Попе холодно, запах странный... Хочу на ручки.'),
            ('опять обед на горшке', 'Опять здесь? Хвостик поджать, еду прижать. Можно в другое место?'),
            ('после еды возьми на ручки', 'Тяну лапки, чтобы обняли, а меня опять сажают есть на горшок. Животик сытый, но всё ещё хочу на ручки.'),
            ('это не миска для еды', 'Я знаю, для чего эта штука! Если назвать её моим столиком, столиком она не станет.'),
            ('завсегдатай туалета', 'Опять еда у унитаза. Ем быстро и надеюсь, что никто сейчас не откроет дверь.'),
            ('личная столовая со смывом', 'У всех появилось постоянное место для еды. В моём есть смыв... Хватит смеяться.'),
            ('не называйте это троном', 'Еда на унитазе. Говорят, королевский пир. Уши горят, но мне совсем не смешно.'),
            ('эта шутка не смешная', 'Снова это сиденье. Берегу хвост и смотрю на дверь: вдруг кто-нибудь войдёт.'),
            ('не давайте столовой имя', 'Да, теперь я ем здесь. Нет, придумывать имя моей туалетной столовой нельзя.'),
            ('достоинство не подают на фарфоре', 'Еда, унитаз и я, изо всех сил делая вид, что эти три вещи сочетаются.'),
            ('кто заказал этот столик', 'Опять место у унитаза. Похоже, в мой заказ входят канализация и зрители.'),
            ('фирменное блюдо — смирение', 'Одна трапеза за другой. Уже знаю, куда положить хвост. Но этому навыку учиться не хотелось.')],
        'SpecialSeat': [
            ('странное место для еды', 'Меня посадили на странное сиденье с едой. Хвостик свернуть. Можно туда, где привычно?'),
            ('снова странный стульчик', 'Опять это сиденье... Прижимаю еду и ищу того, кто меня сюда посадил.'),
            ('лапки всё ещё просят обнять', 'Опять обед здесь. Животик полный, но лапки тянутся: пусть кто-нибудь снимет меня отсюда.'),
            ('у него другая работа', 'Говорят, где можно сидеть, там можно есть. Но эта штука явно нужна для другого.'),
            ('чужое сиденье, тревожный обед', 'Снова еда здесь. Всё кажется, что кто-нибудь придёт воспользоваться этой вещью.'),
            ('столовая на странном сиденье', 'Сиденье с другим назначением стало моим местом для еды. Никто не спросил, хочу ли я этого.'),
            ('не смотрите, как я ем', 'Обед на таком сиденье. Уже слышу будущие шуточки. Дайте спокойно доесть.'),
            ('подработка сиденья — мой позор', 'Ещё одна трапеза. Сначала сиденье делает свою работу, потом заступает на смену унижать меня.'),
            ('шутка стала постоянным местом', 'Столько обедов здесь, что все сразу указывают сюда. Уши каждый раз опускаются.'),
            ('сиденье не на своей работе', 'Обед на мебели для другого дела. Практичное решение, если не учитывать моё достоинство.'),
            ('временное место стало обычным', 'Снова обед здесь. Расстановка всё постояннее, а возражения всё тише.'),
            ('назначено странное место', 'Похоже, теперь я ем здесь. Лучше доесть и уйти, пока не прозвучала очередная насмешка.')],
        'Settings': ['Еда на туалетах и других особых сиденьях', 'Разрешение использовать такие места вместо отдельного стула зависит от статуса едящего раткина и действует также на малышей. По умолчанию включено только для заключённых и рабов, выключено для колонистов и гостей. Обычные стулья предпочтительнее. Для малышей также нужна экспериментальная помощь их категории; прежние настройки помощи сохранены.', 'Раткины-колонисты: еда на особых сиденьях', 'Раткины-гости: еда на особых сиденьях', 'Раткины-заключённые: еда на особых сиденьях', 'Раткины-рабы: еда на особых сиденьях']}}


def append_entries(path, entries):
    text = path.read_text(encoding='utf-8-sig')
    root = ET.fromstring(text)
    for key, value in entries.items():
        old = root.find(key)
        if old is not None:
            assert old.text == value, f'Existing edited entry: {path}: {key}'
    missing = ''.join(f'  <{key}>{escape(value)}</{key}>\n' for key, value in entries.items() if root.find(key) is None)
    path.write_text(text.replace('</LanguageData>', missing + '</LanguageData>'), encoding='utf-8')


def main():
    path = ROOT / '1.6/Defs/ThoughtDefs.xml'
    text = path.read_text(encoding='utf-8-sig')
    root = ET.fromstring(text)
    for group in ('Toilet', 'SpecialSeat'):
        name = 'RDE_' + group + 'AsTable'
        if any(node.findtext('defName') == name for node in root):
            continue
        stages = ''.join('      <li>\n' + f'        <label>{escape(label)}</label>\n'
                         + f'        <description>{escape(description)}</description>\n'
                         + f'        <baseMoodEffect>{-(i % 3 + 1)}</baseMoodEffect>\n      </li>\n'
                         for i, (label, description) in enumerate(TEXTS['English'][group]))
        definition = (f'  <ThoughtDef>\n    <defName>{name}</defName>\n'
                      '    <thoughtClass>RatkinDiningEtiquette.Thought_Memory_RatDining</thoughtClass>\n'
                      '    <durationDays>1</durationDays>\n    <stackLimit>1</stackLimit>\n'
                      '    <developmentalStageFilter>Baby, Child, Adult</developmentalStageFilter>\n'
                      f'    <stages>\n{stages}    </stages>\n  </ThoughtDef>\n')
        text = text.replace('</Defs>', definition + '</Defs>')
    path.write_text(text, encoding='utf-8')
    keys = ['RDE_SpecialSeats', 'RDE_SpecialSeatsTooltip', 'RDE_SpecialSeatsColonists',
            'RDE_SpecialSeatsVisitors', 'RDE_SpecialSeatsPrisoners', 'RDE_SpecialSeatsSlaves']
    babbling = {'Toilet': ['Mabba... eh? Nuu...', 'Mee-eh... nom? Hup...', 'Waa... hup-ba! Nuu-mee!'],
                'SpecialSeat': ['Mee... huh? Mabba...', 'Hup... mee-eh? Nom...', 'Maaa... hup-hup! Mee-eh!']}
    for language, values in TEXTS.items():
        entries = dict(zip(keys, values['Settings']))
        for group, rows in babbling.items():
            entries.update({f'RDE_BabyTalk_{group}_{i}': row for i, row in enumerate(rows)})
        base = ROOT / '1.6/Languages' / language
        append_entries(base / 'Keyed/RatkinDiningEtiquette.xml', entries)
        if language == 'English':
            continue
        entries = {}
        for group in ('Toilet', 'SpecialSeat'):
            for i, (label, description) in enumerate(values[group]):
                entries[f'RDE_{group}AsTable.stages.{i}.label'] = label
                entries[f'RDE_{group}AsTable.stages.{i}.description'] = description
        append_entries(base / 'DefInjected/ThoughtDef/RatkinDiningEtiquette.xml', entries)


if __name__ == '__main__':
    main()
