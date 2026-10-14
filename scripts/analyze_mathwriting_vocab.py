import sys
from datasets import load_dataset
import re

print("Loading MathWriting-human to analyze matrix tokenization...")
try:
    ds = load_dataset("deepcopy/MathWriting-human", split="train", streaming=True)
    matrix_samples = []
    
    for idx, item in enumerate(ds):
        latex = item.get("latex", "")
        if any(kw in latex for kw in ["matrix", "pmatrix", "bmatrix", "\\\\", "\\vec"]):
            matrix_samples.append((idx, latex))
            if len(matrix_samples) >= 30:
                break
                
    print(f"\nCollected {len(matrix_samples)} sample matrix/vector LaTeX strings:")
    for idx, lat in matrix_samples[:10]:
        print(f"[{idx}] {lat}")
        
    # Analyze unique LaTeX commands
    commands = set()
    for _, lat in matrix_samples:
        for cmd in re.findall(r"\\[a-zA-Z]+|\\\\|&|[\[\]\(\)\{\}]", lat):
            commands.add(cmd)
            
    print("\nUnique structural commands found in matrix samples:")
    print(sorted(list(commands)))

except Exception as e:
    print(f"Error: {e}")
    sys.exit(1)
