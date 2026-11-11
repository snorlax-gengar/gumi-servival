using System.Collections.Generic;

/// <summary>
/// 실제 상가 데이터의 프랜차이즈 상표를 지어낸 이름으로 바꾼다 (상표 그대로 쓰지 않기).
/// 지점명("구미역점" 같은 꼬리)은 남겨서 동네 느낌은 살린다. 동네 개인 가게 이름은 그대로.
/// </summary>
public static class Brands
{
    // 긴 것부터 맞춘다 (이마트24R → 이마트24 순서)
    static readonly (string real, string fake)[] Map =
    {
        ("이마트24R", "하루24 "), ("이마트24", "하루24 "), ("세븐일레븐", "하루24 "), ("씨유", "하루24 "), ("CU", "하루24"),
        ("지에스25", "하루24 "), ("GS25", "하루24"),
        ("롯데리아", "버거하우스"), ("노브랜드버거", "버거하우스"), ("맘스터치", "치킨버거집"),
        ("교촌치킨", "황금치킨"), ("네네치킨", "황금치킨"),
        ("투썸플레이스", "카페 오후"), ("메가엠지씨커피", "큰컵커피"), ("빽다방", "큰컵커피"), ("더벤티", "큰컵커피"), ("토프레소", "카페 오후"),
        ("파리바게뜨", "아침빵집"), ("뚜레쥬르", "아침빵집"), ("배스킨라빈스", "아이스크림 가게"), ("설빙", "빙수당"),
        ("씨제이올리브영", "화장품 드럭스토어"), ("뉴발란스", "운동화 매장"), ("구미아디다스", "운동화 매장"),
        ("포토이즘박스", "사진관 네컷"), ("포토이즘", "사진관 네컷"), ("인생네컷", "사진관 네컷"), ("포토그레이", "사진관 네컷"),
        ("KB국민은행", "구미은행"), ("하나은행", "구미은행"),
        ("LG유플러스", "휴대폰 매장"), ("엘지유플러스", "휴대폰 매장"), ("삼성물산에잇세컨즈", "옷가게"),
        ("본죽앤비빔밥카페", "죽집"), ("본죽&비빔밥", "죽집"), ("엽기떡볶이", "떡볶이집"), ("신전떡볶이", "떡볶이집"),
        ("코레일유통", "역 매점"),
    };

    static readonly Dictionary<string, string> cache = new Dictionary<string, string>();

    public static string Scrub(string n)
    {
        if (string.IsNullOrEmpty(n)) return n;
        if (cache.TryGetValue(n, out var r)) return r;
        r = n;
        foreach (var (real, fake) in Map)
        {
            int i = n.IndexOf(real, System.StringComparison.Ordinal);
            if (i < 0) continue;
            string tail = n.Substring(i + real.Length).Trim();
            r = (fake.TrimEnd() + (tail.Length > 0 ? " " + tail : "")).Trim();
            break;
        }
        cache[n] = r;
        return r;
    }

    public static void ScrubAll(RealMapData d)
    {
        if (d == null) return;
        if (d.buildings != null) foreach (var b in d.buildings)
            {
                if (b == null) continue;
                b.n = Scrub(b.n);
                if (b.shops != null) foreach (var s in b.shops) if (s != null) s.n = Scrub(s.n);
            }
        if (d.pois != null) foreach (var p in d.pois) if (p != null) p.n = Scrub(p.n);
    }
}
