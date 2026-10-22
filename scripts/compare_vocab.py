import json
import re

import os

vocab_json = json.load(open('models/math_vocab.json', encoding='utf-8'))['latex_vocabulary']
cs_path = 'Core/Math/OnnxMathVisionEngine.cs' if os.path.exists('Core/Math/OnnxMathVisionEngine.cs') else 'Core/OnnxMathVisionEngine.cs'
with open(cs_path, encoding='utf-8') as f:
    cs = f.read()

m = re.search(r'public static readonly string\[\] Vocabulary = new\[\]\s*\{(.*?)\};', cs, re.DOTALL)
# Match all quoted string literals
raw_tokens = re.findall(r'"((?:\\.|[^"\\])*)"', m.group(1))

print(f"JSON count: {len(vocab_json)}, C# count: {len(raw_tokens)}")
mismatches = 0
for i in range(max(len(vocab_json), len(raw_tokens))):
    j_val = vocab_json[i] if i < len(vocab_json) else "<MISSING>"
    c_val = raw_tokens[i].replace('\\\\', '\\') if i < len(raw_tokens) else "<MISSING>"
    if j_val != c_val:
        print(f"Mismatch at index {i}: JSON='{j_val}' vs C#='{c_val}'")
        mismatches += 1

if mismatches == 0:
    print("ALL 82 TOKENS MATCH PERFECTLY!")
