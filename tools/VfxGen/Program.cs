using System.Drawing;
using System.Drawing.Imaging;

// =====================================================================================
// DOTA2 英雄 VFX 像素贴图生成器
//
// 风格对齐 Pathos 原生特效（如 fire beam / flame strike / acid strike）：
//   - 单格特效在 32×32 像素网格上绘制，最近邻 ×4 放大到 128（与原生 4px 像素颗粒一致）；
//     跨 3×3 旋转的大特效用 64×64 网格（×2），3×3 回退切片用 96×96 网格（每格 32）。
//   - 每种特效只用一条 5~7 色的色阶（暗色作描边 → 亮色作芯），无抗锯齿；
//     过渡与淡出用 4×4 有序抖动（Bayer），边缘用噪声扰动，避免几何规整的「矢量感」。
//   - 底层再垫一层很淡的柔光（与原生 fire beam 的光晕同理），贴图本体仍是硬像素。
//
// 造型与配色对照刀塔百科（wiki.dota2.com.cn）官方技能演示：
//   - 主宰 无敌斩：白热芯 → 金黄 → 橙红刃缘的利刃斩痕，突进残影是断续的金色刀光
//   - 主宰 剑刃风暴：红色新月扫痕（刃头橙金、尾部抖动淡出）+ 细金环 + 钢剑
//   - 主宰 剑舞暴击：淡金长弧刀光 + 命中处红色血花
//   - 斧王 淘汰之刃：红色闪爆 + 白热劈砍 + 金色火星 + 暗红血溅
//   - 斧王 反击螺旋：暗红回旋拖影 + 钢斧 + 甩出的血滴
//   - 斧王 狂战士之吼：参差放射的红色冲击波
//   - 水人 波浪形态：奔涌的青蓝水体 + 白色浪沫与飞沫；落点水花环；适应性打击为水团溅射
//   - 帕吉 肉钩：深灰铁链 + 暗红血迹，钩头为粗铁倒钩，命中处暗红血花
//   - 帕吉 腐烂：以帕吉为中心的黄绿色团状毒雾；肉盾：红褐色血肉外壳；肢解：亮粉白撕咬闪光 + 猩红血溅
//
// 用法：dotnet run --project tools/VfxGen -- <仓库根目录>
// =====================================================================================

string Root = args.Length > 0 ? args[0] : ".";
string Jugg = Path.Combine(Root, "Resources", "Heroes", "juggernaut");
string Axe = Path.Combine(Root, "Resources", "Heroes", "axe");
string Morph = Path.Combine(Root, "Resources", "Heroes", "morphling");
string Pudge = Path.Combine(Root, "Resources", "Heroes", "pudge");

// ---------------------------------------------------------------- 色阶（暗 → 亮）
Color[] Omni = { H("6E1A10"), H("C2401A"), H("EE7C22"), H("FFC03A"), H("FFEB96"), H("FFFFF2") };
Color[] Fury = { H("5E0C10"), H("9C1418"), H("CC261E"), H("EC5228"), H("FF9038"), H("FFCF5A"), H("FFF4D2") };
Color[] Crit = { H("7E3412"), H("C2661E"), H("EAA040"), H("FFD27A"), H("FFF2C8"), H("FFFFFF") };
Color[] Blood = { H("4E080C"), H("8A1016"), H("C42024"), H("EC4A34") };
Color[] Flash = { H("6A0C10"), H("B01A1C"), H("E2382A"), H("FF6A3A"), H("FFAA50") };
Color[] Chop = { H("7A1612"), H("D63A1E"), H("FFA44A"), H("FFEFC8"), H("FFFFFF") };
Color[] Helix = { H("3E070B"), H("6E0E14"), H("A21A20"), H("D2352A"), H("F07448") };
Color[] Roar = { H("5C0A0C"), H("9C1418"), H("D22A22"), H("F2582C"), H("FF9A48"), H("FFDA94") };
Color[] Water = { H("123878"), H("1E60B6"), H("2E8CDE"), H("50C4F0"), H("A6ECFF"), H("FFFFFF") };
Color[] Chain = { H("1E1E24"), H("2E2E34"), H("5A5A62"), H("74747C"), H("8E8E96") };
Color[] Gore = { H("3E0608"), H("5E0C0C"), H("9A1616"), H("C01828") };
Color[] Rot = { H("3A3E10"), H("4E5418"), H("7F8A22"), H("B4B83A"), H("D8D468") };
Color[] Flesh = { H("3E140E"), H("5A1E16"), H("8C3A2A"), H("B85A42"), H("E08A70") };
Color[] Bite = { H("8A1E5A"), H("F070C0"), H("FFB8E6"), H("FFF0FA") };

Color Gold = H("FFCF5A"), GoldDark = H("B87A22"), Ember = H("FFE48A"), White = H("FFFFFF");
Color SteelOut = H("26232C"), SteelMid = H("7A8496"), SteelLight = H("BAC4D2"), SteelEdge = H("F2F6FF");
Color Wood = H("8A5A30"), WoodDark = H("4A2A12");

// =====================================================================================
// 主宰
// =====================================================================================

// 无敌斩斩痕（水平基准，渲染时按攻击方向旋转）：一道微弧利刃 + 下方一道短刀风 + 刃尖火星
{
  var G = new Px(32);
  Field(G, Omni, (X, Y) =>
  {
    var (R, A) = Polar(X, Y, 16, 54);
    float T = (A - (-90 - 24)) / 48f;
    if (T < 0 || T > 1) return 0;
    float W = 3.3f * MathF.Pow(MathF.Sin(MathF.PI * T), 0.8f) * (0.8f + 0.4f * Noise(T * 7, 0, 11));
    return W < 0.4f ? 0 : 1 - MathF.Abs(R - 38) / W;
  });
  Field(G, Omni, (X, Y) =>
  {
    var (R, A) = Polar(X, Y, 14, 60);
    float T = (A - (-90 - 16)) / 30f;
    if (T < 0 || T > 1) return 0;
    float W = 1.7f * MathF.Pow(MathF.Sin(MathF.PI * T), 0.7f);
    return W < 0.4f ? 0 : 0.75f * (1 - MathF.Abs(R - 38) / W);
  });
  Dots(G, (30, 16, White), (29, 15, Ember), (31, 14, Gold), (27, 13, Omni[2]), (25, 12, Omni[3]),
    (2, 16, Omni[2]), (4, 14, Omni[3]), (24, 23, Omni[2]), (9, 25, Omni[1]), (19, 26, Omni[3]));
  Save(G, 4, Omni[3], 0.30f, 5, Jugg, "omnislash fx strike");
}

// 无敌斩突进残影（四方向，可沿路径无缝拼接）：断续的金色刀光 + 零星火星
foreach (var Dir in new[] { "h", "v", "f", "b" })
{
  var G = new Px(32);
  Beam(G, Dir, Omni, (S, D) =>
  {
    float W = 0.7f + 1.1f * Noise(S / 8f, 3, 21, 4);
    if (Noise(S / 4f, 7, 22, 8) < 0.4f) W *= 0.3f;
    float V = 0.9f * (1 - MathF.Abs(D) / W);
    if (V > 0) return V;
    int Si = Wrap((int)MathF.Floor(S), 32), Di = (int)MathF.Floor(D);
    return MathF.Abs(D) < 5 && Hash(Si, Di, 23) > 0.93f ? 0.45f : 0;
  }, 0.5f);
  Save(G, 4, Omni[3], 0.28f, 4, Jugg, $"omnislash fx beam {Dir}");
}

// 剑刃风暴：红色新月扫痕（刃头橙金、尾部抖动淡出）+ 细金环 + 伸出的钢剑；
// 64 网格单张旋转贴图（覆盖 3×3）与 96 网格 3×3 回退切片
Px BladeFury(int N)
{
  var G = new Px(N);
  float K = N / 64f, C = N / 2f;
  Field(G, Fury, (X, Y) =>
  {
    var (R, A) = Polar(X, Y, C, C);
    float Behind = Mod(-90 - A, 360);
    if (Behind > 235) return 0;
    float T = Behind / 235f;
    float Rin = (11 + 13 * T) * K + (Noise(A / 360 * 24 + 24, 1, 31, 24) - 0.5f) * 3 * K;
    float Rout = (29.5f - 2.5f * T) * K + (Noise(A / 360 * 18 + 18, 2, 32, 18) - 0.5f) * 2.5f * K;
    if (R < Rin || R > Rout) return 0;
    float U = (R - Rin) / (Rout - Rin);
    float V = MathF.Pow(1 - T, 1.5f) * (0.5f + 0.5f * U);
    if (Behind < 14) V = MathF.Max(V, 0.9f + 0.1f * U);
    return V;
  }, 0.9f);
  // 细金环（断续）
  for (int Y = 0; Y < N; Y++)
    for (int X = 0; X < N; X++)
    {
      var (R, A) = Polar(X + 0.5f, Y + 0.5f, C, C);
      if (MathF.Abs(R - 31 * K) < 0.55f * K + 0.05f)
      {
        float Nz = Noise(A / 360 * 40 + 40, 5, 33, 40);
        if (Nz > 0.32f) G.Put(X, Y, Nz > 0.6f ? Gold : GoldDark, 0.5f);
      }
    }
  // 钢剑：剑尖朝上（旋转时领先扫痕）
  int Cx = (int)C, Tip = (int)MathF.Round(C - 30 * K), Guard = (int)MathF.Round(C - 10 * K);
  for (int Y = Tip; Y <= Guard; Y++)
  {
    G.Put(Cx - 2, Y, SteelOut); G.Put(Cx + 1, Y, SteelOut);
    G.Put(Cx - 1, Y, Y == Tip ? SteelEdge : SteelLight);
    G.Put(Cx, Y, Y == Tip ? SteelOut : SteelMid);
  }
  G.Put(Cx - 1, Tip - 1, SteelOut);
  G.Put(Cx - 1, Tip + 3, SteelEdge); G.Put(Cx - 1, Tip + 4, SteelEdge);
  for (int X = Cx - 5; X <= Cx + 4; X++) { G.Put(X, Guard + 1, Gold); G.Put(X, Guard + 2, GoldDark); }
  G.Put(Cx - 6, Guard + 1, SteelOut); G.Put(Cx + 5, Guard + 1, SteelOut);
  for (int Y = Guard + 3; Y <= Guard + 6; Y++) { G.Put(Cx - 1, Y, Wood); G.Put(Cx, Y, WoodDark); }
  return G;
}

Save(BladeFury(64), 2, Fury[3], 0.25f, 6, Jugg, "blade fury whirl");
SaveSlices(BladeFury(96), Fury[3], 0.25f, 6, Jugg, "blade fury fx");

// 剑刃风暴命中切痕：短促的红橙刀口
{
  var G = new Px(32);
  Field(G, Fury, (X, Y) =>
  {
    var (R, A) = Polar(X, Y, 16, 42);
    float T = (A - (-90 - 26)) / 52f;
    if (T < 0 || T > 1) return 0;
    float W = 2.6f * MathF.Pow(MathF.Sin(MathF.PI * T), 0.7f) * (0.8f + 0.4f * Noise(T * 6, 1, 35));
    return W < 0.4f ? 0 : 1 - MathF.Abs(R - 26) / W;
  });
  Dots(G, (27, 14, Ember), (28, 12, Fury[4]), (5, 15, Fury[3]), (21, 20, Fury[2]));
  Save(G, 4, Fury[3], 0.25f, 4, Jugg, "blade fury cut");
}

// 剑舞暴击：淡金长弧刀光（刃头厚、尾部细）+ 命中端红色血花（水平基准）
{
  var G = new Px(32);
  Field(G, Crit, (X, Y) =>
  {
    var (R, A) = Polar(X, Y, 15, 30);
    float T = (A - (-165)) / 150f;
    if (T < 0 || T > 1) return 0;
    float W = 4.4f * MathF.Pow(T, 1.1f) * MathF.Pow(1 - T, 0.22f) / 0.82f * (0.85f + 0.3f * Noise(T * 8, 2, 41));
    return W < 0.4f ? 0 : 1 - MathF.Abs(R - 15) / W;
  });
  Field(G, Blood, (X, Y) =>
  {
    float Dx = X - 27, Dy = Y - 25;
    float R = MathF.Sqrt(Dx * Dx + Dy * Dy) / (3.8f + 1.6f * Noise(MathF.Atan2(Dy, Dx) * 1.3f + 8, 1, 42));
    return R < 1 ? 0.95f * (1 - R) + 0.1f : 0;
  }, 0.5f);
  Dots(G, (31, 21, Blood[2]), (30, 30, Blood[1]), (24, 30, Blood[2]), (29, 31, Blood[1]), (31, 26, Blood[3]),
    (23, 21, Blood[2]), (26, 19, Blood[1]), (3, 22, Crit[2]), (6, 16, Crit[3]), (11, 13, Crit[1]));
  Save(G, 4, Crit[3], 0.28f, 5, Jugg, "blade dance fx crit");
}

// =====================================================================================
// 斧王
// =====================================================================================

// 淘汰之刃：红色闪爆 + 自上而下的白热劈砍 + 金色火星 + 下方暗红血溅
{
  var G = new Px(32);
  Field(G, Flash, (X, Y) =>
  {
    float Dx = X - 16, Dy = Y - 17;
    float R = MathF.Sqrt(Dx * Dx + Dy * Dy) / (9.5f + 3f * Noise(MathF.Atan2(Dy, Dx) * 1.6f + 8, 2, 51));
    return R < 1 ? 0.8f * (1 - R) : 0;
  }, 0.9f);
  // 血溅：劈落点一小摊 + 向下、向两侧喷开的参差血线
  Field(G, Blood, (X, Y) =>
  {
    float Dx = X - 16, Dy = Y - 23.5f;
    if (Dx * Dx / 9 + Dy * Dy / 4 < 1) return 0.9f;
    if (Y < 22) return 0;
    float Spread = 3 + (Y - 22) * 0.9f;
    if (MathF.Abs(Dx) > Spread) return 0;
    float Stripe = Noise((X - 16) / Spread * 3.2f + 20, 0, 52);
    float Len = 4 + 7 * Stripe;
    return Stripe > 0.45f && Y - 22 < Len ? 0.8f * (1 - (Y - 22) / Len) + 0.15f : 0;
  }, 0.6f);
  Field(G, Chop, (X, Y) =>
  {
    if (Y > 22) return 0;
    float W = 0.6f + 2.6f * MathF.Pow(Y / 22f, 1.2f);
    float D = MathF.Abs(X - 16.5f + (Y - 11) * 0.12f);
    return 1 - D / W;
  }, 0.5f);
  Dots(G, (6, 12, Ember), (9, 7, Gold), (24, 9, Ember), (27, 14, Gold), (4, 20, GoldDark), (28, 21, Ember),
    (11, 4, Gold), (21, 5, GoldDark), (7, 27, Gold), (25, 28, Ember), (20, 13, White), (12, 15, Ember),
    (10, 30, Blood[1]), (22, 31, Blood[1]), (19, 30, Blood[2]), (13, 31, Blood[0]), (8, 25, Blood[1]), (24, 24, Blood[2]));
  Save(G, 4, Flash[2], 0.32f, 6, Axe, "culling blade fx strike");
}

// 反击螺旋：暗红回旋拖影 + 钢斧（斧刃朝顺时针前进方向）+ 甩出的血滴；64 网格旋转贴图 + 96 网格回退切片
Px CounterHelix(int N)
{
  var G = new Px(N);
  float K = N / 64f, C = N / 2f;
  Field(G, Helix, (X, Y) =>
  {
    var (R, A) = Polar(X, Y, C, C);
    float Behind = Mod(-90 - A, 360);
    if (Behind > 340) return 0;
    float T = Behind / 340f;
    float Half = (4.5f + 2.0f * (Noise(A / 360 * 20 + 20, 3, 61, 20) - 0.5f)) * K;
    float V = (1 - MathF.Abs(R - 25 * K) / Half) * (0.3f + 0.7f * MathF.Pow(1 - T, 1.3f));
    if (V <= 0 && Behind < 220 && (MathF.Abs(R - 17 * K) < 0.5f || MathF.Abs(R - 32 * K) < 0.5f)
      && Noise(A / 360 * 30 + 30, 4, 62, 30) > 0.45f) return 0.22f;
    return V;
  }, 0.9f);
  int Cx = (int)C, Top = (int)MathF.Round(C - 30 * K), Hand = (int)MathF.Round(C - 8 * K);
  for (int Y = Top; Y <= Hand; Y++) { G.Put(Cx - 1, Y, WoodDark); G.Put(Cx, Y, Wood); }
  Sprite(G, Cx + 1, Top, new[]
  {
    "ooo......",
    "omloo....",
    "omllloo..",
    "omlllwwo.",
    "omllllwwo",
    "omllllwwo",
    "omlllwwo.",
    "omlloo...",
    "omoo.....",
    "oo.......",
  }, new() { ['o'] = SteelOut, ['m'] = SteelMid, ['l'] = SteelLight, ['w'] = SteelEdge });
  Sprite(G, Cx - 4, Top + 1, new[] { "..oo", ".oml", "omll", ".oml", "..oo" },
    new() { ['o'] = SteelOut, ['m'] = SteelMid, ['l'] = SteelLight });
  for (int I = 0; I < 7; I++)
  {
    float A = (-90 + 10 + I * 13) * MathF.PI / 180, R = (32 + I % 3 * 1.5f) * K;
    G.Put((int)(C + MathF.Cos(A) * R), (int)(C + MathF.Sin(A) * R), I % 2 == 0 ? Blood[2] : Blood[1]);
  }
  return G;
}

Save(CounterHelix(64), 2, Helix[3], 0.22f, 6, Axe, "counter helix whirl");
SaveSlices(CounterHelix(96), Helix[3], 0.22f, 6, Axe, "counter helix fx");

// 狂战士之吼：参差放射的红色冲击波（内沿最亮，尖刺向外抖动淡出）
Px RoarRing(int N)
{
  var G = new Px(N);
  float K = N / 64f, C = N / 2f;
  Field(G, Roar, (X, Y) =>
  {
    var (R, A) = Polar(X, Y, C, C);
    float Inner = (21 + 1.5f * (Noise(A / 360 * 16 + 16, 1, 71, 16) - 0.5f)) * K;
    float Spike = MathF.Pow(Noise(A / 360 * 22 + 22, 2, 72, 22), 1.6f);
    float Outer = (24 + 10.5f * Spike) * K;
    if (R < Inner) return R > Inner * 0.7f ? 0.3f * (R / Inner - 0.7f) / 0.3f : 0;
    if (R > Outer) return 0;
    return 1.02f - 0.95f * (R - Inner) / (Outer - Inner);
  }, 0.9f);
  return G;
}

Save(RoarRing(64), 2, Roar[2], 0.28f, 6, Axe, "berserkers call ring");
SaveSlices(RoarRing(96), Roar[2], 0.28f, 6, Axe, "berserkers call fx");

// =====================================================================================
// 水人
// =====================================================================================

// 波浪形态水流（四方向，可沿路径无缝拼接）：上缘浪沫、体内速度流线、下缘深色描边、上方飞沫
foreach (var Dir in new[] { "h", "v", "f", "b" })
{
  var G = new Px(32);
  Beam(G, Dir, Water, (S, D) =>
  {
    float Top = -4.5f - 2.4f * Noise(S / 8f, 1, 81, 4) - 2.4f * Noise(S / 4f, 5, 86, 8);
    float Bottom = 4.5f + 1.5f * Noise(S / 8f, 2, 82, 4);
    if (D < Top)
    {
      int Si = Wrap((int)MathF.Floor(S), 32), Di = (int)MathF.Floor(D);
      return D > Top - 4 && Hash(Si, Di, 83) > 0.88f ? 0.8f : 0;
    }
    if (D > Bottom) return 0;
    if (Bottom - D < 1) return 0.1f;
    float U = (D - Top) / (Bottom - Top);
    float V = 0.92f - 0.62f * U;
    if (Noise(S / 16f, MathF.Floor(D) * 0.7f + 10, 84, 2) > 0.64f) V += 0.25f;
    if (D - Top < 1.3f && Noise(S / 4f, 3, 85, 8) > 0.42f) V = 1;
    return V;
  }, 0.6f);
  Save(G, 4, Water[3], 0.25f, 4, Morph, $"waveform fx beam {Dir}");
}

// 波浪形态落点水花（96 网格 3×3）：外缘浪峰最亮、向内抖动淡出，外圈飞溅水滴
{
  var G = new Px(96);
  const float C = 48;
  Field(G, Water, (X, Y) =>
  {
    var (R, A) = Polar(X, Y, C, C);
    float Inner = 24 + 3 * (Noise(A / 360 * 14 + 14, 1, 91, 14) - 0.5f);
    float Outer = 35 + 7 * MathF.Pow(Noise(A / 360 * 26 + 26, 2, 92, 26), 2);
    if (R < Inner) return R > Inner * 0.72f ? 0.25f * (R / Inner - 0.72f) / 0.28f : 0;
    if (R > Outer)
      return R < Outer + 7 && Hash(X > 0 ? (int)X : 0, (int)Y, 93) > 0.97f ? 0.85f : 0;
    float U = (R - Inner) / (Outer - Inner);
    return Outer - R < 1.4f ? 1 : 0.35f + 0.6f * U;
  }, 0.8f);
  SaveSlices(G, Water[3], 0.25f, 6, Morph, "waveform fx");
}

// 适应性打击：水团命中溅射（向上的水冠 + 飞散水滴）
{
  var G = new Px(32);
  Field(G, Water, (X, Y) =>
  {
    var (R, A) = Polar(X, Y, 16, 21);
    bool Crown = A > -165 && A < -15;
    float L = Crown ? 6 + 10 * MathF.Pow(Noise(A / 360 * 14 + 14, 1, 101, 14), 1.3f) : 4.5f + Noise(A / 40 + 9, 2, 102);
    return R < L ? 1.15f - 0.85f * R / L : 0;
  }, 0.6f);
  Dots(G, (4, 8, Water[4]), (27, 6, Water[3]), (8, 3, Water[3]), (24, 2, Water[4]), (2, 17, Water[2]),
    (30, 14, Water[4]), (12, 1, White), (20, 4, White), (6, 26, Water[2]), (27, 25, Water[3]));
  Save(G, 4, Water[3], 0.28f, 5, Morph, "adaptive strike fx strike");
}

// =====================================================================================
// 帕吉
// =====================================================================================

// 肉钩铁链（四方向，可沿路径无缝拼接）：深灰铁环交替（正面椭圆环 / 侧面竖环）+ 零星暗红血滴
foreach (var Dir in new[] { "h", "v", "f", "b" })
{
  var G = new Px(32);
  Beam(G, Dir, Chain, (S, D) =>
  {
    float Local = Mod(S, 8) - 4; // 每 8 像素一节：交替正面环与侧面环
    bool FaceOn = Mod(MathF.Floor(S / 8), 2) == 0;
    float Ring = FaceOn
      ? MathF.Abs(MathF.Sqrt(Local * Local / 16 + D * D / 6.25f) - 0.78f)
      : MathF.Abs(D) < 1.3f && MathF.Abs(Local) < 3.8f ? 0 : 9;
    if (Ring < 0.42f) return FaceOn ? 0.95f - 0.5f * MathF.Max(0, D) / 2.5f : 0.75f;
    int Si = Wrap((int)MathF.Floor(S), 32), Di = (int)MathF.Floor(D);
    return D > 1.5f && D < 5 && Hash(Si, Di, 111) > 0.94f ? -1 : 0;
  }, 0.4f);
  // 血滴（单独上色，避免被铁链色阶覆盖）
  Beam(G, Dir, Gore, (S, D) =>
  {
    int Si = Wrap((int)MathF.Floor(S), 32), Di = (int)MathF.Floor(D);
    return D > 1.5f && D < 5 && Hash(Si, Di, 111) > 0.94f ? 0.7f : 0;
  }, 0.3f);
  Save(G, 4, Chain[1], 0.18f, 3, Pudge, $"meat hook fx beam {Dir}");
}

// 肉钩钩头（水平基准，钩尖朝右；渲染时按出钩方向旋转）：粗铁钩身 + 弯回的倒钩 + 钩尖血迹
{
  var G = new Px(32);
  // 钩身：左侧直柄 → 右侧向下弯回的大弧（中心 (19,17)，半径 7.5，从正上方顺时针绕到左侧）→ 向上翘起的倒钩尖
  Field(G, Chain, (X, Y) =>
  {
    float Shaft = X < 19.5f ? 1 - MathF.Abs(Y - 9.5f) / 1.9f : 0;
    var (R, A) = Polar(X, Y, 19, 17);
    float Sweep = Mod(A + 90, 360); // 0 = 正上方，顺时针增大
    float Arc = Sweep <= 250 ? 1 - MathF.Abs(R - 7.5f) / (1.9f - 0.7f * Sweep / 250f) : 0;
    float Bx = X - 11.5f, By = Y - 13f;
    float Barb = By < 3.5f && By > -3.5f && MathF.Abs(Bx + By * 0.25f) < 1.3f - (3.5f - By) * 0.25f ? 0.9f : 0;
    float V = MathF.Max(Shaft, MathF.Max(Arc, Barb));
    return V <= 0 ? 0 : 0.35f + 0.65f * V * (Y < 13 ? 1 : 0.75f);
  }, 0.5f);
  Field(G, Gore, (X, Y) =>
  {
    var (R, A) = Polar(X, Y, 19, 17);
    float Sweep = Mod(A + 90, 360);
    return Sweep > 120 && Sweep < 250 && MathF.Abs(R - 7.5f) < 1.2f && Noise(Sweep / 12, 0, 115) > 0.45f ? 0.8f : 0;
  }, 0.4f);
  Dots(G, (11, 9, Gore[2]), (16, 27, Gore[1]), (21, 27, Gore[2]), (9, 20, Gore[3]), (13, 26, Gore[1]));
  Save(G, 4, Chain[2], 0.2f, 4, Pudge, "meat hook fx hook");
}

// 肉钩命中血花：中心一摊暗红 + 四散的参差血线与血滴
{
  var G = new Px(32);
  Field(G, Gore, (X, Y) =>
  {
    float Dx = X - 16, Dy = Y - 17;
    var (R, A) = Polar(X, Y, 16, 17);
    float Core = R / (4.5f + 1.5f * Noise(A / 30 + 4, 1, 121));
    if (Core < 1) return 0.95f - 0.4f * Core;
    float Ray = MathF.Pow(Noise(A / 360 * 14 + 14, 2, 122, 14), 2.2f);
    float Len = 6 + 9 * Ray;
    return Ray > 0.25f && R < Len ? 0.75f * (1 - (R - 4) / (Len - 4)) + 0.1f : 0;
  }, 0.6f);
  Dots(G, (3, 6, Gore[1]), (28, 4, Gore[2]), (30, 27, Gore[1]), (5, 28, Gore[2]), (16, 2, Gore[1]), (26, 14, Gore[3]), (8, 15, Gore[3]));
  Save(G, 4, Gore[2], 0.25f, 4, Pudge, "meat hook fx strike");
}

// 腐烂毒雾（96 网格 3×3 九宫格）：黄绿色团状毒云，外缘噪声起伏、内部团块明暗、边缘抖动淡出
{
  var G = new Px(96);
  const float C = 48;
  Field(G, Rot, (X, Y) =>
  {
    var (R, A) = Polar(X, Y, C, C);
    float Edge = 36 + 9 * (Noise(A / 360 * 9 + 9, 1, 131, 9) - 0.5f) + 5 * (Noise(A / 360 * 23 + 23, 2, 132, 23) - 0.5f);
    if (R > Edge) return 0;
    float Puff = Noise(X / 9f, Y / 9f, 133) * 0.6f + Noise(X / 4.5f, Y / 4.5f, 134) * 0.4f;
    float Fade = MathF.Min(1, (Edge - R) / 10f);
    return (0.25f + 0.75f * Puff) * Fade * (R < 12 ? 0.6f + R / 30f : 1);
  }, 0.9f);
  SaveSlices(G, Rot[2], 0.22f, 6, Pudge, "pudge rot fx");
}

// 肉盾：包裹帕吉的红褐色血肉外壳（缝线 + 鼓起的肉块），中间留空露出角色
{
  var G = new Px(32);
  Field(G, Flesh, (X, Y) =>
  {
    var (R, A) = Polar(X, Y, 16, 17);
    float Inner = 9.5f + 1.5f * Noise(A / 360 * 10 + 10, 1, 141, 10);
    float Outer = 14 + 1.8f * Noise(A / 360 * 7 + 7, 2, 142, 7);
    if (R < Inner || R > Outer) return 0;
    float Lump = Noise(X / 3.5f, Y / 3.5f, 143);
    float U = (R - Inner) / (Outer - Inner);
    return 0.35f + 0.55f * Lump * (1 - MathF.Abs(U - 0.45f));
  }, 0.7f);
  // 粗缝线（深色短横线沿环分布）
  for (int I = 0; I < 9; I++)
  {
    float A = I * 40 * MathF.PI / 180, R = 11.8f;
    int Cx = (int)(16 + MathF.Cos(A) * R), Cy = (int)(17 + MathF.Sin(A) * R);
    G.Put(Cx, Cy, Flesh[0], 3); G.Put(Cx + 1, Cy, Flesh[0], 3);
  }
  Save(G, 4, Flesh[2], 0.22f, 4, Pudge, "meat shield fx strike");
}

// 肢解：亮粉白撕咬闪光（上下两排「牙口」咬合）+ 四溅的猩红血花
{
  var G = new Px(32);
  Field(G, Gore, (X, Y) =>
  {
    var (R, A) = Polar(X, Y, 16, 16);
    float Ray = MathF.Pow(Noise(A / 360 * 12 + 12, 1, 151, 12), 1.8f);
    float Len = 7 + 9 * Ray;
    return R < Len && Ray > 0.18f ? 0.85f * (1 - R / Len) + 0.12f : 0;
  }, 0.6f);
  Field(G, Bite, (X, Y) =>
  {
    float Dx = X - 16, Dy = Y - 16;
    float R = MathF.Sqrt(Dx * Dx + Dy * Dy * 1.6f) / (8 + 2 * Noise(MathF.Atan2(Dy, Dx) * 2 + 6, 1, 152));
    return R < 1 ? 1.05f - R : 0;
  }, 0.8f);
  // 牙口：上下两排白色三角齿
  for (int I = 0; I < 5; I++)
  {
    int X0 = 9 + I * 3;
    G.Put(X0, 11, White, 3); G.Put(X0 + 1, 11, White, 3); G.Put(X0, 12, White, 3);
    G.Put(X0 + 1, 21, White, 3); G.Put(X0, 21, White, 3); G.Put(X0 + 1, 20, White, 3);
  }
  Dots(G, (2, 9, Gore[2]), (29, 6, Gore[1]), (30, 24, Gore[2]), (4, 27, Gore[1]), (15, 1, Gore[3]), (18, 30, Gore[2]));
  Save(G, 4, Bite[2], 0.3f, 5, Pudge, "dismember fx strike");
}

Console.WriteLine("VFX generated.");
return;

// =====================================================================================
// 像素绘制工具
// =====================================================================================

static Color H(string Hex) => ColorTranslator.FromHtml("#" + Hex);

static float Mod(float V, float M) => ((V % M) + M) % M;

static int Wrap(int V, int M) => ((V % M) + M) % M;

/// <summary>像素中心 → (半径, 角度°)，角度 0 = 向右，屏幕坐标下顺时针为正。</summary>
static (float R, float A) Polar(float X, float Y, float Cx, float Cy)
{
  float Dx = X - Cx, Dy = Y - Cy;
  return (MathF.Sqrt(Dx * Dx + Dy * Dy), MathF.Atan2(Dy, Dx) * 180 / MathF.PI);
}

static float Hash(int X, int Y, int Seed)
{
  unchecked
  {
    uint Hv = (uint)(X * 374761393 + Y * 668265263 + Seed * 1442695041);
    Hv = (Hv ^ (Hv >> 13)) * 1274126177u;
    Hv ^= Hv >> 16;
    return (Hv & 0xFFFF) / 65535f;
  }
}

/// <summary>平滑值噪声；Period &gt; 0 时在 X 方向按该周期回绕（用于可拼接的光束与环形角度）。</summary>
static float Noise(float X, float Y, int Seed, int Period = 0)
{
  int X0 = (int)MathF.Floor(X), Y0 = (int)MathF.Floor(Y);
  float Fx = X - X0, Fy = Y - Y0;
  Fx = Fx * Fx * (3 - 2 * Fx);
  Fy = Fy * Fy * (3 - 2 * Fy);
  int Wx(int V) => Period > 0 ? Wrap(V, Period) : V;
  float A = Hash(Wx(X0), Y0, Seed), B = Hash(Wx(X0 + 1), Y0, Seed);
  float C = Hash(Wx(X0), Y0 + 1, Seed), D = Hash(Wx(X0 + 1), Y0 + 1, Seed);
  return (A + (B - A) * Fx) + ((C + (D - C) * Fx) - (A + (B - A) * Fx)) * Fy;
}

static float Bayer(int X, int Y)
{
  int[] M = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
  return (M[(Y & 3) * 4 + (X & 3)] + 0.5f) / 16f;
}

/// <summary>按强度场 V(x,y)∈[0,1] 上色：强度映射到色阶，色阶交界与淡出处用有序抖动。</summary>
static void Field(Px G, Color[] Ramp, Func<float, float, float> V, float Dither = 0.7f)
{
  for (int Y = 0; Y < G.N; Y++)
    for (int X = 0; X < G.N; X++)
    {
      float Val = V(X + 0.5f, Y + 0.5f);
      if (Val <= 0) continue;
      int I = (int)MathF.Floor(Val * Ramp.Length + (Bayer(X, Y) - 0.5f) * Dither);
      if (I < 0) continue;
      if (I >= Ramp.Length) I = Ramp.Length - 1;
      G.Put(X, Y, Ramp[I], (I + 1) / (float)Ramp.Length);
    }
}

/// <summary>光束格：F(S, D)，S 为沿光束方向坐标（每格周期 32，保证相邻格无缝），D 为垂直光束的有符号距离。</summary>
static void Beam(Px G, string Dir, Color[] Ramp, Func<float, float, float> F, float Dither)
{
  const float Rt2 = 1.41421356f;
  Field(G, Ramp, (X, Y) => Dir switch
  {
    "h" => F(X, Y - 16),
    "v" => F(Y, X - 16),
    "f" => F((X - Y) / 2 + 16, (X + Y - 32) / Rt2),
    _ => F((X + Y) / 2, (Y - X) / Rt2),
  }, Dither);
}

static void Dots(Px G, params (int X, int Y, Color C)[] List)
{
  foreach (var (X, Y, C) in List) G.Put(X, Y, C, 1.5f);
}

/// <summary>手绘像素块：字符画逐像素贴入，'.' 为透明。</summary>
static void Sprite(Px G, int Ox, int Oy, string[] Rows, Dictionary<char, Color> Pal)
{
  for (int Y = 0; Y < Rows.Length; Y++)
    for (int X = 0; X < Rows[Y].Length; X++)
      if (Pal.TryGetValue(Rows[Y][X], out var C)) G.Put(Ox + X, Oy + Y, C);
}

/// <summary>像素网格 → 最近邻放大位图，底层垫一层很淡的同色柔光。</summary>
static Bitmap Render(Px G, int Scale, Color Glow, float GlowAlpha, int GlowRadius)
{
  int S = G.N * Scale;
  var Mask = new float[S * S];
  for (int Y = 0; Y < S; Y++)
    for (int X = 0; X < S; X++)
      Mask[Y * S + X] = G[X / Scale, Y / Scale].A > 0 ? 1 : 0;
  for (int Pass = 0; Pass < 3; Pass++) BoxBlur(Mask, S, GlowRadius);

  var Bmp = new Bitmap(S, S, PixelFormat.Format32bppArgb);
  for (int Y = 0; Y < S; Y++)
    for (int X = 0; X < S; X++)
    {
      var P = G[X / Scale, Y / Scale];
      if (P.A > 0) Bmp.SetPixel(X, Y, P);
      else
      {
        int A = (int)MathF.Round(Math.Clamp(Mask[Y * S + X] * GlowAlpha, 0, 1) * 255);
        if (A > 2) Bmp.SetPixel(X, Y, Color.FromArgb(A, Glow));
      }
    }
  return Bmp;
}

static void BoxBlur(float[] M, int S, int R)
{
  var T = new float[M.Length];
  for (int Y = 0; Y < S; Y++)
    for (int X = 0; X < S; X++)
    {
      float Sum = 0;
      for (int K = -R; K <= R; K++) { int Xx = X + K; if (Xx >= 0 && Xx < S) Sum += M[Y * S + Xx]; }
      T[Y * S + X] = Sum / (2 * R + 1);
    }
  for (int Y = 0; Y < S; Y++)
    for (int X = 0; X < S; X++)
    {
      float Sum = 0;
      for (int K = -R; K <= R; K++) { int Yy = Y + K; if (Yy >= 0 && Yy < S) Sum += T[Yy * S + X]; }
      M[Y * S + X] = Sum / (2 * R + 1);
    }
}

static void Save(Px G, int Scale, Color Glow, float GlowAlpha, int GlowRadius, string Dir, string Name)
{
  using var Bmp = Render(G, Scale, Glow, GlowAlpha, GlowRadius);
  Directory.CreateDirectory(Dir);
  string P = Path.Combine(Dir, Name + ".png");
  Bmp.Save(P, ImageFormat.Png);
  Console.WriteLine($"  {P}");
}

/// <summary>96 网格 3×3 大图 → 切成 tl/tc/tr/ml/mc/mr/bl/bc/br 9 张 128 贴图（原生 Explosion 回退用）。</summary>
static void SaveSlices(Px G, Color Glow, float GlowAlpha, int GlowRadius, string Dir, string Prefix)
{
  using var Big = Render(G, 4, Glow, GlowAlpha, GlowRadius);
  string[] Names = { "tl", "tc", "tr", "ml", "mc", "mr", "bl", "bc", "br" };
  Directory.CreateDirectory(Dir);
  for (int I = 0; I < 9; I++)
  {
    using var Part = Big.Clone(new Rectangle((I % 3) * 128, (I / 3) * 128, 128, 128), PixelFormat.Format32bppArgb);
    string P = Path.Combine(Dir, $"{Prefix} {Names[I]}.png");
    Part.Save(P, ImageFormat.Png);
    Console.WriteLine($"  {P}");
  }
}

/// <summary>像素网格：每个像素记录所用色阶亮度，叠加时只允许更亮（或手绘强制）覆盖，避免后画的暗边盖掉亮芯。</summary>
sealed class Px
{
  public readonly int N;
  readonly Color[,] Cells;
  readonly float[,] Level;

  public Px(int N)
  {
    this.N = N;
    Cells = new Color[N, N];
    Level = new float[N, N];
  }

  public Color this[int X, int Y] => X >= 0 && Y >= 0 && X < N && Y < N ? Cells[X, Y] : Color.Transparent;

  public void Put(int X, int Y, Color C, float Lvl = 2f)
  {
    if (X < 0 || Y < 0 || X >= N || Y >= N || Lvl < Level[X, Y]) return;
    Cells[X, Y] = C;
    Level[X, Y] = Lvl;
  }
}
