import onnxruntime as ort
import numpy as np

print('Loading ONNX session...')
sess = ort.InferenceSession('scripts/test_decoder.onnx')

print('Testing length 3 (same as dummy):')
out = sess.run(None, {
    'src': np.zeros((1, 64, 256), dtype=np.float32),
    'src_mask': np.zeros((1, 64), dtype=bool),
    'tgt': np.ones((1, 3), dtype=np.int64)
})[0]
print('Length 3 success, output shape:', out.shape)

for length in [1, 2, 4, 10]:
    try:
        out = sess.run(None, {
            'src': np.zeros((1, 64, 256), dtype=np.float32),
            'src_mask': np.zeros((1, 64), dtype=bool),
            'tgt': np.ones((1, length), dtype=np.int64)
        })[0]
        print(f'Length {length} success, output shape:', out.shape)
    except Exception as e:
        print(f'Length {length} failed: {e}')
