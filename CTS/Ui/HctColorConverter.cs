namespace CircleToSearch.Ui;

using System.Windows.Media;

internal static class HctColorConverter
{
    private static readonly double[][] ScaledDiscountFromLinearRgb =
    {
        new[] { 0.001200833568784504, 0.002389694492170889, 0.0002795742885861124 },
        new[] { 0.0005891086651375999, 0.0029785502573438758, 0.0003270666104008398 },
        new[] { 0.00010146692491640572, 0.0005364214359186694, 0.0032979401770712076 },
    };

    private static readonly double[][] LinearRgbFromScaledDiscount =
    {
        new[] { 1373.2198709594231, -1100.4251190754821, -7.278681089101213 },
        new[] { -271.815969077903, 559.6580465940733, -32.46047482791194 },
        new[] { 1.9622899599665666, -57.173814538844006, 308.7233197812385 },
    };

    private static readonly double[] YFromLinearRgb = { 0.2126, 0.7152, 0.0722 };
    private static readonly ViewingConditions DefaultViewingConditions = CreateDefaultViewingConditions();

    public static Color Convert(Color source, double chroma, double tone)
    {
        var linearRgb = new[]
        {
            Linearized(source.R),
            Linearized(source.G),
            Linearized(source.B),
        };
        var hue = HueOf(linearRgb) * 180.0 / Math.PI;
        var rgb = SolveToRgb(hue, chroma, tone);
        return Color.FromArgb(source.A, rgb.R, rgb.G, rgb.B);
    }

    internal static double ToneOf(Color color)
    {
        var y = YFromLinearRgb[0] * Linearized(color.R)
                + YFromLinearRgb[1] * Linearized(color.G)
                + YFromLinearRgb[2] * Linearized(color.B);
        return 116.0 * LabF(y / 100.0) - 16.0;
    }

    private static Rgb SolveToRgb(double hueDegrees, double chroma, double tone)
    {
        if (chroma < 0.0001 || tone < 0.0001 || tone > 99.9999)
            return RgbFromTone(tone);

        hueDegrees = SanitizeDegrees(hueDegrees);
        var hueRadians = hueDegrees / 180.0 * Math.PI;
        var y = YFromTone(tone);
        var exact = FindResultByJ(hueRadians, chroma, y);
        return exact ?? RgbFromLinear(BisectToLimit(y, hueRadians));
    }

    private static Rgb? FindResultByJ(double hueRadians, double chroma, double y)
    {
        var viewing = DefaultViewingConditions;
        var j = Math.Sqrt(y) * 11.0;
        var tInnerCoefficient = 1.0 / Math.Pow(1.64 - Math.Pow(0.29, viewing.N), 0.73);
        var eHue = 0.25 * (Math.Cos(hueRadians + 2.0) + 3.8);
        var p1 = eHue * (50000.0 / 13.0) * viewing.Nc * viewing.Ncb;
        var hueSin = Math.Sin(hueRadians);
        var hueCos = Math.Cos(hueRadians);

        for (var iteration = 0; iteration < 5; iteration++)
        {
            var normalizedJ = j / 100.0;
            var alpha = chroma == 0.0 || j == 0.0 ? 0.0 : chroma / Math.Sqrt(normalizedJ);
            var t = Math.Pow(alpha * tInnerCoefficient, 1.0 / 0.9);
            var ac = viewing.Aw * Math.Pow(normalizedJ, 1.0 / viewing.C / viewing.Z);
            var p2 = ac / viewing.Nbb;
            var gamma = 23.0 * (p2 + 0.305) * t
                        / (23.0 * p1 + 11.0 * t * hueCos + 108.0 * t * hueSin);
            var a = gamma * hueCos;
            var b = gamma * hueSin;
            var adaptedR = (460.0 * p2 + 451.0 * a + 288.0 * b) / 1403.0;
            var adaptedG = (460.0 * p2 - 891.0 * a - 261.0 * b) / 1403.0;
            var adaptedB = (460.0 * p2 - 220.0 * a - 6300.0 * b) / 1403.0;
            var linearRgb = Multiply(
                new[]
                {
                    InverseChromaticAdaptation(adaptedR),
                    InverseChromaticAdaptation(adaptedG),
                    InverseChromaticAdaptation(adaptedB),
                },
                LinearRgbFromScaledDiscount);

            if (linearRgb[0] < 0 || linearRgb[1] < 0 || linearRgb[2] < 0)
                return null;

            var resultingY = YFromLinearRgb[0] * linearRgb[0]
                             + YFromLinearRgb[1] * linearRgb[1]
                             + YFromLinearRgb[2] * linearRgb[2];
            if (resultingY <= 0)
                return null;

            if (iteration == 4 || Math.Abs(resultingY - y) < 0.002)
            {
                if (linearRgb[0] > 100.01 || linearRgb[1] > 100.01 || linearRgb[2] > 100.01)
                    return null;
                return RgbFromLinear(linearRgb);
            }

            j -= (resultingY - y) * j / (2.0 * resultingY);
        }

        return null;
    }

    private static double[] BisectToLimit(double y, double targetHue)
    {
        var segment = BisectToSegment(y, targetHue);
        var left = segment[0];
        var leftHue = HueOf(left);
        var right = segment[1];

        for (var axis = 0; axis < 3; axis++)
        {
            if (left[axis] == right[axis]) continue;

            int leftPlane;
            int rightPlane;
            if (left[axis] < right[axis])
            {
                leftPlane = CriticalPlaneBelow(TrueDelinearized(left[axis]));
                rightPlane = CriticalPlaneAbove(TrueDelinearized(right[axis]));
            }
            else
            {
                leftPlane = CriticalPlaneAbove(TrueDelinearized(left[axis]));
                rightPlane = CriticalPlaneBelow(TrueDelinearized(right[axis]));
            }

            for (var iteration = 0; iteration < 8 && Math.Abs(rightPlane - leftPlane) > 1; iteration++)
            {
                var middlePlane = (int)Math.Floor((leftPlane + rightPlane) / 2.0);
                var middle = SetCoordinate(left, CriticalPlane(middlePlane), right, axis);
                var middleHue = HueOf(middle);
                if (AreInCyclicOrder(leftHue, targetHue, middleHue))
                {
                    right = middle;
                    rightPlane = middlePlane;
                }
                else
                {
                    left = middle;
                    leftHue = middleHue;
                    leftPlane = middlePlane;
                }
            }
        }

        return Midpoint(left, right);
    }

    private static double[][] BisectToSegment(double y, double targetHue)
    {
        var left = new[] { -1.0, -1.0, -1.0 };
        var right = left;
        var leftHue = 0.0;
        var rightHue = 0.0;
        var initialized = false;
        var uncut = true;

        for (var index = 0; index < 12; index++)
        {
            var middle = NthVertex(y, index);
            if (middle[0] < 0) continue;

            var middleHue = HueOf(middle);
            if (!initialized)
            {
                left = middle;
                right = middle;
                leftHue = middleHue;
                rightHue = middleHue;
                initialized = true;
                continue;
            }

            if (!uncut && !AreInCyclicOrder(leftHue, middleHue, rightHue)) continue;

            uncut = false;
            if (AreInCyclicOrder(leftHue, targetHue, middleHue))
            {
                right = middle;
                rightHue = middleHue;
            }
            else
            {
                left = middle;
                leftHue = middleHue;
            }
        }

        return new[] { left, right };
    }

    private static double[] NthVertex(double y, int index)
    {
        var coordinateA = index % 4 <= 1 ? 0.0 : 100.0;
        var coordinateB = index % 2 == 0 ? 0.0 : 100.0;
        if (index < 4)
        {
            var green = coordinateA;
            var blue = coordinateB;
            var red = (y - green * YFromLinearRgb[1] - blue * YFromLinearRgb[2]) / YFromLinearRgb[0];
            return IsBounded(red) ? new[] { red, green, blue } : InvalidVertex();
        }

        if (index < 8)
        {
            var blue = coordinateA;
            var red = coordinateB;
            var green = (y - red * YFromLinearRgb[0] - blue * YFromLinearRgb[2]) / YFromLinearRgb[1];
            return IsBounded(green) ? new[] { red, green, blue } : InvalidVertex();
        }

        {
            var red = coordinateA;
            var green = coordinateB;
            var blue = (y - red * YFromLinearRgb[0] - green * YFromLinearRgb[1]) / YFromLinearRgb[2];
            return IsBounded(blue) ? new[] { red, green, blue } : InvalidVertex();
        }
    }

    private static double HueOf(double[] linearRgb)
    {
        var discounted = Multiply(linearRgb, ScaledDiscountFromLinearRgb);
        var adaptedR = ChromaticAdaptation(discounted[0]);
        var adaptedG = ChromaticAdaptation(discounted[1]);
        var adaptedB = ChromaticAdaptation(discounted[2]);
        var a = (11.0 * adaptedR - 12.0 * adaptedG + adaptedB) / 11.0;
        var b = (adaptedR + adaptedG - 2.0 * adaptedB) / 9.0;
        return Math.Atan2(b, a);
    }

    private static ViewingConditions CreateDefaultViewingConditions()
    {
        var whitePoint = new[] { 95.047, 100.0, 108.883 };
        var adaptingLuminance = 200.0 / Math.PI * YFromTone(50.0) / 100.0;
        var backgroundTone = 50.0;
        var surround = 2.0;
        var coneR = 0.401288 * whitePoint[0] + 0.650173 * whitePoint[1] - 0.051461 * whitePoint[2];
        var coneG = -0.250268 * whitePoint[0] + 1.204414 * whitePoint[1] + 0.045854 * whitePoint[2];
        var coneB = -0.002079 * whitePoint[0] + 0.048952 * whitePoint[1] + 0.953127 * whitePoint[2];
        var f = 0.8 + surround / 10.0;
        var c = f >= 0.9
            ? Lerp(0.59, 0.69, (f - 0.9) * 10.0)
            : Lerp(0.525, 0.59, (f - 0.8) * 10.0);
        var d = Math.Clamp(
            f * (1.0 - 1.0 / 3.6 * Math.Exp((-adaptingLuminance - 42.0) / 92.0)),
            0.0,
            1.0);
        var rgbD = new[]
        {
            d * (100.0 / coneR) + 1.0 - d,
            d * (100.0 / coneG) + 1.0 - d,
            d * (100.0 / coneB) + 1.0 - d,
        };
        var k = 1.0 / (5.0 * adaptingLuminance + 1.0);
        var k4 = k * k * k * k;
        var fl = k4 * adaptingLuminance
                 + 0.1 * (1.0 - k4) * (1.0 - k4) * Math.Cbrt(5.0 * adaptingLuminance);
        var n = YFromTone(backgroundTone) / whitePoint[1];
        var z = 1.48 + Math.Sqrt(n);
        var nbb = 0.725 / Math.Pow(n, 0.2);
        var adaptedWhiteR = AdaptedWhite(fl, rgbD[0], coneR);
        var adaptedWhiteG = AdaptedWhite(fl, rgbD[1], coneG);
        var adaptedWhiteB = AdaptedWhite(fl, rgbD[2], coneB);
        var aw = (2.0 * adaptedWhiteR + adaptedWhiteG + 0.05 * adaptedWhiteB) * nbb;
        return new ViewingConditions(n, aw, nbb, nbb, c, f, z);
    }

    private static double AdaptedWhite(double fl, double discount, double cone) =>
        400.0 * Math.Pow(fl * discount * cone / 100.0, 0.42)
        / (Math.Pow(fl * discount * cone / 100.0, 0.42) + 27.13);

    private static double[] Multiply(double[] row, double[][] matrix) =>
        new[]
        {
            row[0] * matrix[0][0] + row[1] * matrix[0][1] + row[2] * matrix[0][2],
            row[0] * matrix[1][0] + row[1] * matrix[1][1] + row[2] * matrix[1][2],
            row[0] * matrix[2][0] + row[1] * matrix[2][1] + row[2] * matrix[2][2],
        };

    private static double ChromaticAdaptation(double component)
    {
        var absolute = Math.Pow(Math.Abs(component), 0.42);
        return Math.Sign(component) * 400.0 * absolute / (absolute + 27.13);
    }

    private static double InverseChromaticAdaptation(double adapted)
    {
        var absolute = Math.Abs(adapted);
        var basis = Math.Max(0.0, 27.13 * absolute / (400.0 - absolute));
        return Math.Sign(adapted) * Math.Pow(basis, 1.0 / 0.42);
    }

    private static double CriticalPlane(int index) => Linearized((index + 0.5) / 255.0);
    private static int CriticalPlaneBelow(double value) => (int)Math.Floor(value - 0.5);
    private static int CriticalPlaneAbove(double value) => (int)Math.Ceiling(value - 0.5);
    private static bool IsBounded(double value) => value is >= 0.0 and <= 100.0;
    private static double[] InvalidVertex() => new[] { -1.0, -1.0, -1.0 };

    private static bool AreInCyclicOrder(double a, double b, double c) =>
        SanitizeRadians(b - a) < SanitizeRadians(c - a);

    private static double SanitizeRadians(double angle) =>
        (angle + Math.PI * 8.0) % (Math.PI * 2.0);

    private static double SanitizeDegrees(double degrees)
    {
        degrees %= 360.0;
        return degrees < 0.0 ? degrees + 360.0 : degrees;
    }

    private static double[] SetCoordinate(double[] source, double coordinate, double[] target, int axis)
    {
        var amount = (coordinate - source[axis]) / (target[axis] - source[axis]);
        return new[]
        {
            source[0] + (target[0] - source[0]) * amount,
            source[1] + (target[1] - source[1]) * amount,
            source[2] + (target[2] - source[2]) * amount,
        };
    }

    private static double[] Midpoint(double[] a, double[] b) =>
        new[] { (a[0] + b[0]) / 2.0, (a[1] + b[1]) / 2.0, (a[2] + b[2]) / 2.0 };

    private static double Linearized(byte component) => Linearized(component / 255.0);

    private static double Linearized(double normalized) => normalized <= 0.040449936
        ? normalized / 12.92 * 100.0
        : Math.Pow((normalized + 0.055) / 1.055, 2.4) * 100.0;

    private static double TrueDelinearized(double component)
    {
        var normalized = component / 100.0;
        var delinearized = normalized <= 0.0031308
            ? normalized * 12.92
            : 1.055 * Math.Pow(normalized, 1.0 / 2.4) - 0.055;
        return delinearized * 255.0;
    }

    private static Rgb RgbFromLinear(double[] linearRgb) =>
        new(Delinearized(linearRgb[0]), Delinearized(linearRgb[1]), Delinearized(linearRgb[2]));

    private static byte Delinearized(double component) =>
        (byte)Math.Clamp((int)Math.Round(TrueDelinearized(component), MidpointRounding.AwayFromZero), 0, 255);

    private static Rgb RgbFromTone(double tone)
    {
        var component = Delinearized(YFromTone(tone));
        return new Rgb(component, component, component);
    }

    private static double YFromTone(double tone) => 100.0 * LabInverse((tone + 16.0) / 116.0);

    private static double LabF(double value)
    {
        const double epsilon = 216.0 / 24389.0;
        const double kappa = 24389.0 / 27.0;
        return value > epsilon ? Math.Cbrt(value) : (kappa * value + 16.0) / 116.0;
    }

    private static double LabInverse(double value)
    {
        const double epsilon = 216.0 / 24389.0;
        const double kappa = 24389.0 / 27.0;
        var cube = value * value * value;
        return cube > epsilon ? cube : (116.0 * value - 16.0) / kappa;
    }

    private static double Lerp(double start, double stop, double amount) =>
        (1.0 - amount) * start + amount * stop;

    private readonly record struct Rgb(byte R, byte G, byte B);
    private readonly record struct ViewingConditions(
        double N,
        double Aw,
        double Nbb,
        double Ncb,
        double C,
        double Nc,
        double Z);
}
