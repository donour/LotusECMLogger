"""Survey lookup_* call sites in a Ghidra decompile: what kind of expression feeds each table input.

Usage: python tools/lookup_survey.py references/C132E0278.c out.tsv

Prints a summary (input kinds, tables with several call sites) and writes one TSV row per lookup input.
Findings are summarised in CCP-client-plan.md section 5. This is a text-level parse of decompiled C, so
counts are approximate and the enclosing-function column is a heuristic. SIGS maps each lookup variant's
argument positions and must be updated if the decompile's signatures change.
"""
import re, sys, collections

src = open(sys.argv[1], encoding="utf-8", errors="replace").read()

# function name -> (index of lut arg, indices of input args, indices of axis args)
SIGS = {
    "lookup_2D_uint8_fixed": (2, [1], []),
    "lookup_3D_uint8": (6, [2, 3], [4, 5]),
    "lookup_2D_uint8_interpolated": (2, [1], [3]),
    "lookup_2D_uint16_interpolated": (2, [1], [3]),
    "lookup_2D_uint8_interpolated_noaxis": (2, [1], []),
    "lookup_3D_uint8_interpolated": (4, [2, 3], [5, 6]),
    "lookup_3D_uint32_interpolated": (4, [2, 3], [5, 6]),
    "lookup_2D_uint16_interpolated_noaxis_scale_8bit_to_10bit": (2, [1], []),
}

def split_args(s):
    out, depth, cur = [], 0, ""
    for ch in s:
        if ch in "([": depth += 1
        if ch in ")]": depth -= 1
        if ch == "," and depth == 0:
            out.append(cur.strip()); cur = ""
        else:
            cur += ch
    out.append(cur.strip())
    return out

# function bodies: name of enclosing function per offset
func_starts = [(m.start(), m.group(1)) for m in re.finditer(r"^\S[^\n;]*?\b(\w+)\s*\([^;{]*?\)\s*\n\s*\n?\{", src, re.M)]

def enclosing(pos):
    name = "?"
    for p, n in func_starts:
        if p > pos: break
        name = n
    return name

GLOBAL = re.compile(r"^\(?(\([\w ]+\*?\))?\s*&?([A-Za-z_]\w*)(\[[^\]]+\])?\)?$")
LOCAL = re.compile(r"^(u|i|b|s|c|f|d)?Var\d+$|^param_\d+$|^local_\w+$|^[a-z]Stack_\w+$|^p\w*Var\d+$|^extraout_\w+$")

def classify(expr):
    e = re.sub(r"\((u?int\d+_t|ushort|uint|byte|short|char|int|undefined\d?|longlong|ulonglong)\)", "", expr).strip()
    e = e.strip()
    while e.startswith("(") and e.endswith(")"):
        e = e[1:-1].strip()
    m = re.fullmatch(r"[A-Za-z_]\w*", e)
    if m:
        return ("local" if LOCAL.match(e) else "global", e)
    if re.fullmatch(r"[A-Za-z_]\w*\[[^\]]+\]", e):
        return ("global[idx]", e)
    if re.fullmatch(r"0x[0-9a-fA-F]+|\d+", e):
        return ("const", e)
    names = re.findall(r"[A-Za-z_]\w*", e)
    if any(LOCAL.match(n) for n in names):
        return ("expr(local)", e)
    if "(" in e and re.search(r"\w+\(", e):
        return ("call", e)
    return ("expr(globals)", e)

kinds = collections.Counter()
examples = collections.defaultdict(list)
per_table = collections.defaultdict(set)
rows = []
for m in re.finditer(r"\b(lookup_\w+)\s*\(", src):
    fn = m.group(1)
    if fn not in SIGS: continue
    # skip definitions
    line_start = src.rfind("\n", 0, m.start()) + 1
    if src[line_start] not in " \t": continue
    i, depth = m.end(), 1
    while depth:
        if src[i] == "(": depth += 1
        elif src[i] == ")": depth -= 1
        i += 1
    args = split_args(src[m.end():i - 1])
    lut_i, in_i, ax_i = SIGS[fn]
    if len(args) <= lut_i: continue
    lut = re.sub(r"\(.*?\)", "", args[lut_i]).strip("&* ")
    for k, ai in enumerate(in_i):
        kind, e = classify(args[ai])
        kinds[kind] += 1
        if len(examples[kind]) < 12: examples[kind].append(f"{lut} [{'XY'[k]}] <- {args[ai]}   in {enclosing(m.start())}")
        per_table[(lut, k)].add(args[ai])
        rows.append((fn, lut, "XY"[k], kind, args[ai], enclosing(m.start())))

total = sum(kinds.values())
print(f"input arguments: {total}")
for k, n in kinds.most_common():
    print(f"  {k:14s} {n:4d}  {100*n/total:5.1f}%")
for k in kinds:
    print(f"\n== {k}")
    for ex in examples[k]: print("   ", ex)

multi = {t: v for t, v in per_table.items() if len(v) > 1}
print(f"\ntables/axes looked up with >1 distinct input expression: {len(multi)}")
for (t, k), v in list(multi.items())[:10]:
    print(f"   {t} [{'XY'[k]}]: {sorted(v)}")

luts = collections.Counter(r[1] for r in rows if r[2] == "X")
print(f"\ndistinct tables referenced: {len(luts)}; tables with >1 call site: {sum(1 for c in luts.values() if c > 1)}")
nonsym = [l for l in luts if not l.startswith("CAL_")]
print(f"lut args that are not CAL_ symbols: {len(nonsym)} e.g. {nonsym[:10]}")

with open(sys.argv[2], "w", encoding="utf-8") as f:
    f.write("fn\ttable\taxis\tkind\texpr\tfunction\n")
    for r in rows: f.write("\t".join(r) + "\n")
