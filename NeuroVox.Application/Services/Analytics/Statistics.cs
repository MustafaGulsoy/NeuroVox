namespace NeuroVox.Application.Services.Analytics
{
    public static class Statistics
    {
        public static double CohensKappa(IReadOnlyList<int> a, IReadOnlyList<int> b)
        {
            if (a.Count != b.Count || a.Count == 0) throw new ArgumentException("Ratings required");
            var labels = a.Concat(b).Distinct().ToList();
            double po = a.Zip(b).Count(p => p.First == p.Second) / (double)a.Count;
            double pe = labels.Sum(l => a.Count(x => x == l) / (double)a.Count * b.Count(x => x == l) / (double)b.Count);
            return Math.Abs(1 - pe) < 1e-12 ? 0 : (po - pe) / (1 - pe);
        }

        public static double FleissKappa(int[,] counts, int nSubjects, int kRaters)
        {
            int nCategories = counts.GetLength(1);
            var pJ = new double[nCategories];
            for (int c = 0; c < nCategories; c++)
            {
                int sum = 0;
                for (int s = 0; s < nSubjects; s++) sum += counts[s, c];
                pJ[c] = sum / (double)(nSubjects * kRaters);
            }
            double pBar = 0;
            for (int s = 0; s < nSubjects; s++)
            {
                int sumSq = 0;
                for (int c = 0; c < nCategories; c++) sumSq += counts[s, c] * counts[s, c];
                pBar += (sumSq - kRaters) / (double)(kRaters * (kRaters - 1));
            }
            pBar /= nSubjects;
            double pE = pJ.Sum(p => p * p);
            return Math.Abs(1 - pE) < 1e-12 ? 0 : (pBar - pE) / (1 - pE);
        }

        public static double WeightedKappa(IReadOnlyList<int> a, IReadOnlyList<int> b)
        {
            if (a.Count != b.Count || a.Count == 0) throw new ArgumentException("Ratings required");
            var cats = a.Concat(b).Distinct().OrderBy(x => x).ToList();
            int k = cats.Count;
            var idx = cats.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i);
            double[,] conf = new double[k, k];
            foreach (var (va, vb) in a.Zip(b)) conf[idx[va], idx[vb]]++;
            double[,] w = new double[k, k];
            for (int i = 0; i < k; i++) for (int j = 0; j < k; j++) w[i, j] = Math.Pow(i - j, 2) / Math.Pow(k - 1, 2);
            var margA = new double[k]; var margB = new double[k];
            for (int i = 0; i < k; i++)
            {
                for (int j = 0; j < k; j++) { margA[i] += conf[i, j]; margB[j] += conf[i, j]; }
            }
            double num = 0, den = 0;
            for (int i = 0; i < k; i++) for (int j = 0; j < k; j++)
            {
                num += w[i, j] * conf[i, j];
                den += w[i, j] * margA[i] * margB[j] / a.Count;
            }
            return Math.Abs(den) < 1e-12 ? 0 : 1 - num / den;
        }

        public static double IccTwoWayRandomSingle(double[,] x)
        {
            int n = x.GetLength(0), k = x.GetLength(1);
            if (n < 2 || k < 2) throw new ArgumentException("Need >=2 subjects and >=2 raters");
            double grand = 0;
            for (int i = 0; i < n; i++) for (int j = 0; j < k; j++) grand += x[i, j];
            grand /= n * k;
            double ssRows = 0, ssCols = 0, ssTotal = 0;
            for (int i = 0; i < n; i++)
            {
                double rowMean = 0;
                for (int j = 0; j < k; j++) rowMean += x[i, j];
                rowMean /= k;
                ssRows += k * Math.Pow(rowMean - grand, 2);
            }
            for (int j = 0; j < k; j++)
            {
                double colMean = 0;
                for (int i = 0; i < n; i++) colMean += x[i, j];
                colMean /= n;
                ssCols += n * Math.Pow(colMean - grand, 2);
            }
            for (int i = 0; i < n; i++) for (int j = 0; j < k; j++) ssTotal += Math.Pow(x[i, j] - grand, 2);
            double ssError = ssTotal - ssRows - ssCols;
            double msRows = ssRows / (n - 1), msCols = ssCols / (k - 1), msError = ssError / ((n - 1) * (k - 1));
            double denom = msRows + (k - 1) * msError + k * (msCols - msError) / n;
            return Math.Abs(denom) < 1e-12 ? 0 : (msRows - msError) / denom;
        }

        public static double Pearson(IReadOnlyList<double> x, IReadOnlyList<double> y)
        {
            int n = x.Count;
            if (n != y.Count || n < 2) throw new ArgumentException("Need >=2 paired values");
            double mx = x.Average(), my = y.Average();
            double num = 0, dx = 0, dy = 0;
            for (int i = 0; i < n; i++)
            {
                num += (x[i] - mx) * (y[i] - my);
                dx += Math.Pow(x[i] - mx, 2);
                dy += Math.Pow(y[i] - my, 2);
            }
            double den = Math.Sqrt(dx * dy);
            return Math.Abs(den) < 1e-12 ? 0 : num / den;
        }

        public static double Spearman(IReadOnlyList<double> x, IReadOnlyList<double> y)
        {
            return Pearson(Rank(x), Rank(y));
        }

        private static List<double> Rank(IReadOnlyList<double> v)
        {
            var order = v.Select((val, idx) => (val, idx)).OrderBy(t => t.val).ToList();
            var ranks = new double[v.Count];
            for (int i = 0; i < order.Count; i++)
            {
                int j = i;
                while (j + 1 < order.Count && order[j + 1].val == order[i].val) j++;
                double avg = (i + 1 + j + 1) / 2.0;
                for (int m = i; m <= j; m++) ranks[order[m].idx] = avg;
                i = j;
            }
            return ranks.ToList();
        }

        // Linear slope over months since first measurement.
        public static double SlopeMonths(IReadOnlyList<double> months, IReadOnlyList<double> values)
        {
            return RegressSlope(months, values);
        }

        public static double RegressSlope(IReadOnlyList<double> x, IReadOnlyList<double> y)
        {
            double mx = x.Average(), my = y.Average();
            double num = 0, den = 0;
            for (int i = 0; i < x.Count; i++) { num += (x[i] - mx) * (y[i] - my); den += Math.Pow(x[i] - mx, 2); }
            return Math.Abs(den) < 1e-12 ? 0 : num / den;
        }
    }
}
