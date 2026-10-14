import sys
from datasets import load_dataset

print("Connecting to Hugging Face dataset deepcopy/MathWriting-human (streaming mode)...")
try:
    ds = load_dataset("deepcopy/MathWriting-human", split="train", streaming=True)
    print("Connected successfully! Scanning for matrix samples...")
    
    count = 0
    for idx, item in enumerate(ds):
        latex = item.get("latex", "")
        # Check if formula contains matrix structure
        if any(kw in latex for kw in ["matrix", "pmatrix", "bmatrix", "\\\\"]):
            print(f"\n[Matrix Sample #{count+1} at index {idx}]")
            print(f"LaTeX: {latex}")
            keys = list(item.keys())
            print(f"Available fields: {keys}")
            if "image" in item and item["image"] is not None:
                img = item["image"]
                print(f"Image format: {type(img)}, size: {img.size if hasattr(img, 'size') else 'N/A'}")
            count += 1
            if count >= 3:
                break
                
        if idx >= 2000 and count == 0:
            print("Checked first 2000 samples, no matrix found in this slice.")
            break
            
    print(f"\nDone! Found {count} matrix samples.")

except Exception as e:
    print(f"Error during inspection: {e}")
    sys.exit(1)
