using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// =====================================================================================
// DOTA2 英雄 VFX 贴图生成器
//
// 所有特效贴图在 64×64「逻辑像素」画布上用 8× 超采样矢量绘制，再缩到 64×64 并以最近邻放大到 128×128，
// 与项目现有 2px 像素颗粒的贴图风格一致。颜色/造型参考 DOTA2 官方技能演示：
//   - 主宰 无敌斩：白芯 + 冰蓝外缘的细长利刃斩痕，残影为淡蓝细线
//   - 主宰 剑刃风暴：剑刃环绕旋转（钢白刃 + 金色刃光 + 风痕），中心镂空露出英雄
//   - 主宰 剑舞暴击：红橙新月刀光 + 速度线
//   - 斧王 淘汰之刃：自上而下的重斧劈砍（猩红 + 白热刃口）
//   - 斧王 反击螺旋：猩红斧刃回旋弧
//   - 斧王 狂战士之吼：红橙怒吼冲击环
//   - 水人 波浪形态：半透明水流带（深蓝 → 青 + 白色浪尖）与落点水花环
//
// 用法：dotnet run --project tools/VfxGen -- <仓库根目录>
// =====================================================================================

const int Logical = 64;     // 每格逻辑像素
const int Supersample = 8;  // 超采样倍数
const int OutputScale = 2;  // 输出放大倍数（64 → 128）

string Root = args.Length > 0 ? args[0] : ".";
string Jugg = Path.Combine(Root, "Resources", "Heroes", "juggernaut");
string Axe = Path.Combine(Root, "Resources", "Heroes", "axe");
string Morph = Path.Combine(Root, "Resources", "Heroes", "morphling");

// ---------------------------------------------------------------- 调色板
var White = Color.FromArgb(255, 255, 255, 255);
var IceCore = Color.FromArgb(255, 236, 250, 255);
var IceMid = Color.FromArgb(200, 150, 226, 255);
var IceGlow = Color.FromArgb(70, 80, 170, 255);
var Steel = Color.FromArgb(255, 244, 247, 255);
var Gold = Color.FromArgb(220, 255, 196, 84);
var GoldGlow = Color.FromArgb(70, 255, 150, 40);
var CritCore = Color.FromArgb(255, 255, 236, 190);
var CritMid = Color.FromArgb(235, 255, 84, 34);
var CritGlow = Color.FromArgb(90, 200, 16, 40);
var BloodCore = Color.FromArgb(255, 255, 240, 200);
var BloodMid = Color.FromArgb(240, 222, 30, 30);
var BloodDark = Color.FromArgb(200, 120, 6, 16);
var BloodGlow = Color.FromArgb(80, 170, 0, 20);
var RoarCore = Color.FromArgb(235, 255, 120, 40);
var RoarGlow = Color.FromArgb(80, 210, 30, 20);
var WaterDeep = Color.FromArgb(90, 30, 110, 255);
var WaterBody = Color.FromArgb(170, 60, 190, 255);
var WaterLight = Color.FromArgb(210, 150, 235, 255);
var Foam = Color.FromArgb(235, 245, 252, 255);

// =====================================================================================
// 主宰
// =====================================================================================

// 无敌斩斩痕（水平基准，渲染时按攻击方向旋转 + 随机抖动）
Save(Tile(G =>
{
  // 主斩：贯穿整格中线的细长利刃（大半径、小张角，微弧），中段最厚、两端尖
  var Main = Arc(32, 30 + 92, 92, -90 - 18, 36, Symmetric(4.2f, 0.55f));
  Layer(G, Main, (3.6f, IceGlow), (2.0f, IceMid), (1.0f, IceCore));
  // 副斩：下方一道更短更细的刀风余光
  var Echo = Arc(30, 38 + 80, 80, -90 - 12, 24, Symmetric(1.6f, 0.7f));
  Layer(G, Echo, (2.4f, IceGlow), (1.0f, IceMid));
  // 刃尖星芒
  Spark(G, 60.0f, 34.0f, 3.6f, IceMid, White);
}), Jugg, "omnislash fx strike");

// 无敌斩突进残影（四方向细线）
foreach (var (Name, Angle) in BeamDirections())
{
  Save(Tile(G => StreakAcross(G, Angle, Symmetric(2.4f, 0.25f), (3.2f, IceGlow), (1.7f, IceMid), (0.7f, IceCore))),
    Jugg, $"omnislash fx beam {Name}");
}

// 剑刃风暴：主宰伸臂持剑高速自转 —— 1 把实体长剑 + 2 道剑影，每把剑身后拖一片渐隐的扇形剑痕
// （单张贴图，渲染时覆盖 3×3 并按顺时针持续旋转，中心镂空露出英雄本体）
Bitmap BladeFuryWhirl(int Size)
{
  return Canvas(Size, G =>
  {
    float C = Size / 2.0f, K = Size / 64.0f;
    float Hilt = Size * 0.15f, Guard = Size * 0.19f, Tip = Size * 0.47f;
    for (int I = 0; I < 3; I++)
    {
      float Theta = -90 + I * 120;
      float Alpha = I == 0 ? 1.0f : 0.42f;

      // 扇形剑痕：剑身扫过的区域，越往后越淡（顺时针旋转 → 痕迹在逆时针一侧）
      const int Slices = 28;
      float TrailSweep = 105;
      for (int S = 0; S < Slices; S++)
      {
        float T0 = S / (float)Slices;
        float Fade = MathF.Pow(1 - T0, 1.8f) * Alpha;
        float A0 = Theta - TrailSweep * (S + 1) / Slices, Sweep = TrailSweep / Slices + 0.6f;
        float Mid = (Guard + Tip) * 0.5f + Size * 0.02f, Band = (Tip - Guard) * (0.9f - 0.35f * T0);
        Layer(G, Arc(C, C, Mid, A0, Sweep, _ => Band), (1.0f, Fade01(Color.FromArgb(70, 255, 214, 130), Fade)));
        // 剑尖划出的亮线
        Layer(G, Arc(C, C, Tip - 0.6f * K, A0, Sweep, _ => 1.3f * K), (1.0f, Fade01(Color.FromArgb(210, 255, 240, 205), Fade)));
      }

      // 剑身：护手 → 剑尖的细长三角刃，钢白刃身 + 金色刃光 + 白色剑脊
      float Rad = Theta * MathF.PI / 180;
      float Ux = MathF.Cos(Rad), Uy = MathF.Sin(Rad);
      PointF At(float R) => new(C + Ux * R, C + Uy * R);
      var BladeShape = Ribbon(T => At(Guard + (Tip - Guard) * T), T => 3.0f * K * MathF.Pow(1 - T, 0.55f) + 0.4f * K);
      Layer(G, BladeShape, (2.4f, Fade01(GoldGlow, Alpha)), (1.5f, Fade01(Gold, Alpha)), (1.0f, Fade01(Steel, Alpha)));
      Layer(G, Ribbon(T => At(Guard + (Tip - Guard) * 0.92f * T), _ => 0.6f * K), (1.0f, Fade01(White, Alpha)));
      if (I == 0)
      {
        // 护手与剑柄（仅实体剑）
        var Gp = At(Guard);
        Layer(G, Streak(Gp.X - Uy * 3.2f * K, Gp.Y + Ux * 3.2f * K, Gp.X + Uy * 3.2f * K, Gp.Y - Ux * 3.2f * K, _ => 1.5f * K),
          (1.0f, Color.FromArgb(255, 214, 160, 60)));
        Layer(G, Ribbon(T => At(Hilt + (Guard - Hilt) * T), _ => 1.4f * K), (1.0f, Color.FromArgb(255, 120, 70, 40)));
      }
    }
  });
}

SaveRaw(Downscale(BladeFuryWhirl(Logical * OutputScale)), Jugg, "blade fury whirl");
SaveSlices(BladeFuryWhirl(Logical * 3), Jugg, "blade fury fx");

// 剑刃风暴命中切痕（小号细斩）
Save(Tile(G =>
{
  var Cut = Arc(32, 32 + 54, 54, -90 - 20, 40, Symmetric(2.4f, 0.7f));
  Layer(G, Cut, (2.8f, GoldGlow), (1.6f, Gold), (0.8f, Steel));
}), Jugg, "blade fury cut");

// 剑舞暴击：红橙新月刀光 + 速度线（水平基准）
Save(Tile(G =>
{
  var Crescent = Arc(28, 34, 22, -150, 190, Leading(7.5f, 0.55f, 0.35f));
  Layer(G, Crescent, (2.2f, CritGlow), (1.45f, CritMid), (0.55f, CritCore));
  var Rim = Arc(28, 34, 25.5f, -110, 120, Symmetric(1.3f, 0.6f));
  Layer(G, Rim, (1.0f, CritCore));
  for (int I = 0; I < 3; I++)
  {
    float Y = 20 + I * 12;
    Layer(G, Streak(4 + I * 3, Y, 18 + I * 2, Y, Symmetric(1.1f, 0.4f)), (1.0f, Color.FromArgb(150, 255, 160, 90)));
  }
}), Jugg, "blade dance fx crit");

// =====================================================================================
// 斧王
// =====================================================================================

// 淘汰之刃：自上而下的重斧劈砍 + 劈裂冲击纹
Save(Tile(G =>
{
  var Chop = Arc(-26, 32, 60, -36, 72, Leading(10.0f, 0.6f, 0.2f));
  Layer(G, Chop, (2.0f, BloodGlow), (1.35f, BloodDark), (0.85f, BloodMid), (0.3f, BloodCore));
  // 劈落点横向冲击与裂纹
  Layer(G, Streak(12, 50, 52, 50, Symmetric(2.0f, 0.5f)), (2.4f, BloodGlow), (1.0f, Color.FromArgb(200, 255, 120, 80)));
  foreach (var (Dx, Dy) in new[] { (-14f, 8f), (12f, 9f), (-7f, 12f), (7f, 13f) })
    Layer(G, Streak(32, 50, 32 + Dx, 50 + Dy, Symmetric(1.2f, 0.5f)), (1.0f, BloodDark));
}), Axe, "culling blade fx strike");

// 反击螺旋：猩红斧刃回旋弧（单张，渲染时覆盖 3×3 并高速旋转）
Bitmap CounterHelixWhirl(int Size)
{
  return Canvas(Size, G =>
  {
    float C = Size / 2.0f, R = Size * 0.39f, K = Size / 64.0f;
    Layer(G, Arc(C, C, R, 0, 290, Leading(7.0f * K, 1.3f, 0.12f)), (1.9f, BloodGlow), (1.35f, BloodDark), (0.9f, BloodMid), (0.3f, BloodCore));
    Layer(G, Arc(C, C, R * 0.7f, 150, 160, Symmetric(1.6f * K, 0.5f)), (1.6f, BloodGlow), (1.0f, Color.FromArgb(150, 255, 110, 80)));
  });
}

SaveRaw(Downscale(CounterHelixWhirl(Logical * OutputScale)), Axe, "counter helix whirl");
SaveSlices(CounterHelixWhirl(Logical * 3), Axe, "counter helix fx");

// 狂战士之吼：怒吼冲击环（单张，渲染时由小放大）
Bitmap RoarRing(int Size)
{
  return Canvas(Size, G =>
  {
    float C = Size / 2.0f, R = Size * 0.40f, K = Size / 64.0f;
    Layer(G, Arc(C, C, R, 0, 360, _ => 4.2f * K), (2.4f, RoarGlow), (1.0f, RoarCore), (0.35f, Color.FromArgb(230, 255, 214, 150)));
    // 外沿 10 道放射状怒吼尖刺
    for (int I = 0; I < 10; I++)
    {
      float A = I * 36 + 8, Rad = A * MathF.PI / 180;
      float X0 = C + MathF.Cos(Rad) * (R + 2 * K), Y0 = C + MathF.Sin(Rad) * (R + 2 * K);
      float X1 = C + MathF.Cos(Rad) * (R + 7 * K), Y1 = C + MathF.Sin(Rad) * (R + 7 * K);
      Layer(G, Streak(X0, Y0, X1, Y1, Symmetric(1.6f * K, 0.3f)), (1.8f, RoarGlow), (1.0f, RoarCore));
    }
  });
}

SaveRaw(Downscale(RoarRing(Logical * OutputScale)), Axe, "berserkers call ring");
SaveSlices(RoarRing(Logical * 3), Axe, "berserkers call fx");

// =====================================================================================
// 水人
// =====================================================================================

// 波浪形态水流带（四方向）
foreach (var (Name, Angle) in BeamDirections())
{
  Save(Tile(G =>
  {
    float Len = Angle % 90 == 0 ? 64 : 64 * 1.42f;
    var Saved = G.Save();
    G.TranslateTransform(32, 32);
    G.RotateTransform(Angle);
    PointF Center(float T) => new(-Len / 2 + Len * T, MathF.Sin(T * MathF.PI * 4) * 3.0f);
    Layer(G, Ribbon(Center, _ => 10.0f), (1.5f, WaterDeep), (1.0f, WaterBody), (0.5f, WaterLight));
    // 浪尖白沫：沿波峰的一条细线
    PointF Crest(float T) => new(-Len / 2 + Len * T, MathF.Sin(T * MathF.PI * 4) * 3.0f - 3.6f);
    Layer(G, Ribbon(Crest, T => 1.6f * (0.4f + 0.6f * MathF.Abs(MathF.Sin(T * MathF.PI * 4)))), (1.0f, Foam));
    G.Restore(Saved);
  }), Morph, $"waveform fx beam {Name}");
}

// 波浪形态落点水花（3×3）
SaveSlices(Canvas(Logical * 3, G =>
{
  float C = Logical * 1.5f, R = Logical * 1.05f;
  Layer(G, Arc(C, C, R, 0, 360, _ => 16f), (1.6f, WaterDeep), (1.0f, WaterBody), (0.35f, WaterLight));
  Layer(G, Arc(C, C, R + 7, 0, 360, T => 2.4f * (0.5f + 0.5f * MathF.Sin(T * MathF.PI * 18))), (1.0f, Foam));
  var Rnd = new Random(42);
  for (int I = 0; I < 26; I++)
  {
    float A = (float)(Rnd.NextDouble() * Math.PI * 2);
    float D = R + 12 + (float)Rnd.NextDouble() * 26;
    float S = 2.5f + (float)Rnd.NextDouble() * 3.5f;
    Dot(G, C + MathF.Cos(A) * D, C + MathF.Sin(A) * D, S, Color.FromArgb(190, 120, 220, 255), Foam);
  }
}), Morph, "waveform fx");

Console.WriteLine("VFX generated.");
return;

// =====================================================================================
// 绘制工具
// =====================================================================================

static IEnumerable<(string Name, float Angle)> BeamDirections()
{
  yield return ("h", 0);
  yield return ("v", 90);
  yield return ("f", -45); // 「/」东北-西南
  yield return ("b", 45);  // 「\」西北-东南
}

static Bitmap Canvas(int Size, Action<Graphics> Draw)
{
  var Bmp = new Bitmap(Size * Supersample, Size * Supersample, PixelFormat.Format32bppPArgb);
  using var G = Graphics.FromImage(Bmp);
  G.Clear(Color.Transparent);
  G.SmoothingMode = SmoothingMode.AntiAlias;
  G.CompositingQuality = CompositingQuality.HighQuality;
  G.ScaleTransform(Supersample, Supersample);
  Draw(G);
  return Bmp;
}

static Bitmap Tile(Action<Graphics> Draw) => Downscale(Canvas(Logical, Draw));

/// <summary>超采样画布 → 逻辑尺寸（高质量缩小）。</summary>
static Bitmap Downscale(Bitmap Src)
{
  int W = Src.Width / Supersample, H = Src.Height / Supersample;
  var Dst = new Bitmap(W, H, PixelFormat.Format32bppPArgb);
  using (var G = Graphics.FromImage(Dst))
  {
    G.Clear(Color.Transparent);
    G.InterpolationMode = InterpolationMode.HighQualityBicubic;
    G.PixelOffsetMode = PixelOffsetMode.HighQuality;
    G.CompositingMode = CompositingMode.SourceCopy;
    G.DrawImage(Src, new Rectangle(0, 0, W, H));
  }
  Src.Dispose();
  return Dst;
}

/// <summary>逻辑尺寸 → 输出尺寸（最近邻放大，保留像素颗粒）。</summary>
static Bitmap Upscale(Bitmap Src)
{
  var Dst = new Bitmap(Src.Width * OutputScale, Src.Height * OutputScale, PixelFormat.Format32bppArgb);
  using var G = Graphics.FromImage(Dst);
  G.Clear(Color.Transparent);
  G.InterpolationMode = InterpolationMode.NearestNeighbor;
  G.PixelOffsetMode = PixelOffsetMode.Half;
  G.CompositingMode = CompositingMode.SourceCopy;
  G.DrawImage(Src, new Rectangle(0, 0, Dst.Width, Dst.Height));
  return Dst;
}

static void Save(Bitmap Logical64, string Dir, string Name)
{
  using var Out = Upscale(Logical64);
  Logical64.Dispose();
  Directory.CreateDirectory(Dir);
  string P = Path.Combine(Dir, Name + ".png");
  Out.Save(P, ImageFormat.Png);
  Console.WriteLine($"  {P}");
}

/// <summary>3×3 超采样大图 → 切成 tl/tc/tr/ml/mc/mr/bl/bc/br 9 张贴图（原生 Explosion 回退用）。</summary>
static void SaveSlices(Bitmap Big, string Dir, string Prefix)
{
  using var Small = Downscale(Big);
  string[] Names = { "tl", "tc", "tr", "ml", "mc", "mr", "bl", "bc", "br" };
  for (int I = 0; I < 9; I++)
  {
    var Part = new Bitmap(Logical, Logical, PixelFormat.Format32bppPArgb);
    using (var G = Graphics.FromImage(Part))
    {
      G.CompositingMode = CompositingMode.SourceCopy;
      G.DrawImage(Small, new Rectangle(0, 0, Logical, Logical), new Rectangle((I % 3) * Logical, (I / 3) * Logical, Logical, Logical), GraphicsUnit.Pixel);
    }
    Save(Part, Dir, $"{Prefix} {Names[I]}");
  }
}

/// <summary>两端尖、中间厚的对称宽度曲线。</summary>
static Func<float, float> Symmetric(float Width, float Sharpness)
  => T => Width * MathF.Pow(MathF.Max(0, MathF.Sin(MathF.PI * T)), Sharpness);

/// <summary>前端（T→1）厚、尾端细的刃形宽度曲线，末端仍收成尖。</summary>
static Func<float, float> Leading(float Width, float Rise, float Fall)
{
  float Peak = Rise / (Rise + Fall);
  float Max = MathF.Pow(Peak, Rise) * MathF.Pow(1 - Peak, Fall);
  return T => Width * MathF.Pow(Math.Clamp(T, 0, 1), Rise) * MathF.Pow(Math.Clamp(1 - T, 0, 1), Fall) / Max;
}

/// <summary>以 (Cx,Cy) 为圆心、半径 R 的弧形条带（角度：度，0 = 向右，顺时针）。</summary>
static Func<float, Func<float, float>, PointF[]> Arc(float Cx, float Cy, float R, float StartDeg, float SweepDeg, Func<float, float> Width)
{
  return (Scale, _) => RibbonPoints(
    T =>
    {
      float A = (StartDeg + SweepDeg * T) * MathF.PI / 180;
      return new PointF(Cx + MathF.Cos(A) * R, Cy + MathF.Sin(A) * R);
    },
    T => Width(T) * Scale);
}

static Func<float, Func<float, float>, PointF[]> Streak(float X0, float Y0, float X1, float Y1, Func<float, float> Width)
  => (Scale, _) => RibbonPoints(T => new PointF(X0 + (X1 - X0) * T, Y0 + (Y1 - Y0) * T), T => Width(T) * Scale);

static Func<float, Func<float, float>, PointF[]> Ribbon(Func<float, PointF> Center, Func<float, float> Width)
  => (Scale, _) => RibbonPoints(Center, T => Width(T) * Scale);

/// <summary>沿中心线、按宽度函数生成闭合条带多边形。</summary>
static PointF[] RibbonPoints(Func<float, PointF> Center, Func<float, float> Width, int Samples = 96)
{
  var Left = new List<PointF>();
  var Right = new List<PointF>();
  for (int I = 0; I <= Samples; I++)
  {
    float T = I / (float)Samples;
    var P = Center(T);
    var Q = Center(Math.Clamp(T + 0.002f, 0, 1));
    var Pm = Center(Math.Clamp(T - 0.002f, 0, 1));
    float Dx = Q.X - Pm.X, Dy = Q.Y - Pm.Y;
    float L = MathF.Max(1e-5f, MathF.Sqrt(Dx * Dx + Dy * Dy));
    float Nx = -Dy / L, Ny = Dx / L;
    float H = Width(T) * 0.5f;
    Left.Add(new PointF(P.X + Nx * H, P.Y + Ny * H));
    Right.Add(new PointF(P.X - Nx * H, P.Y - Ny * H));
  }
  Right.Reverse();
  Left.AddRange(Right);
  return Left.ToArray();
}

/// <summary>由外到内叠加多层（宽度倍率, 颜色），模拟辉光 → 刃身 → 白芯。</summary>
static void Layer(Graphics G, Func<float, Func<float, float>, PointF[]> Shape, params (float Scale, Color Colour)[] Layers)
{
  foreach (var (Scale, Colour) in Layers)
  {
    using var B = new SolidBrush(Colour);
    G.FillPolygon(B, Shape(Scale, null), FillMode.Winding);
  }
}

static void StreakAcross(Graphics G, float AngleDeg, Func<float, float> Width, params (float Scale, Color Colour)[] Layers)
{
  float Len = AngleDeg % 90 == 0 ? 64 : 64 * 1.42f;
  var State = G.Save();
  G.TranslateTransform(32, 32);
  G.RotateTransform(AngleDeg);
  Layer(G, Streak(-Len / 2, 0, Len / 2, 0, T => Width(0.5f) * (0.55f + 0.45f * MathF.Sin(MathF.PI * T))), Layers);
  G.Restore(State);
}

static Color Fade01(Color C, float Multiplier)
  => Color.FromArgb(Math.Clamp((int)MathF.Round(C.A * Multiplier), 0, 255), C.R, C.G, C.B);

static void Spark(Graphics G, float X, float Y, float R, Color Outer, Color Inner)
{
  Layer(G, Streak(X - R, Y, X + R, Y, Symmetric(1.4f, 0.8f)), (1.8f, Outer), (0.8f, Inner));
  Layer(G, Streak(X, Y - R * 0.7f, X, Y + R * 0.7f, Symmetric(1.1f, 0.8f)), (1.8f, Outer), (0.8f, Inner));
}

static void Dot(Graphics G, float X, float Y, float R, Color Outer, Color Inner)
{
  using (var B = new SolidBrush(Outer))
    G.FillEllipse(B, X - R, Y - R, R * 2, R * 2);
  using (var B = new SolidBrush(Inner))
    G.FillEllipse(B, X - R * 0.45f, Y - R * 0.6f, R * 0.8f, R * 0.8f);
}

/// <summary>已是输出尺寸（128×128）的贴图直接保存，不做像素放大（用于跨 3×3 绘制的旋转环，保留细节）。</summary>
static void SaveRaw(Bitmap Image128, string Dir, string Name)
{
  Directory.CreateDirectory(Dir);
  string P = Path.Combine(Dir, Name + ".png");
  Image128.Save(P, ImageFormat.Png);
  Image128.Dispose();
  Console.WriteLine($"  {P}");
}
