import json

vocab_json = json.load(open('models/math_vocab.json', encoding='utf-8'))['latex_vocabulary']
print(f"Total tokens: {len(vocab_json)}")

lines = []
chunk = []
for x in vocab_json:
    esc = x.replace('\\', '\\\\')
    chunk.append(f'"{esc}"')
    if len(chunk) == 10:
        lines.append('            ' + ', '.join(chunk) + ',')
        chunk = []
if chunk:
    lines.append('            ' + ', '.join(chunk))

print("public static readonly string[] Vocabulary = new[]\n        {\n" + '\n'.join(lines) + "\n        };")
