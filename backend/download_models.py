import os
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
os.environ["HF_HOME"] = str(ROOT / "runtime" / "hf-cache")
os.environ["HF_HUB_DISABLE_TELEMETRY"] = "1"
os.environ["DO_NOT_TRACK"] = "1"
from huggingface_hub import snapshot_download
MODELS = {
    "whisper": ("dropbox-dash/faster-whisper-large-v3-turbo", "0a363e9161cbc7ed1431c9597a8ceaf0c4f78fcf"),
    "cleanup": ("Qwen/Qwen3-4B-Instruct-2507", "cdbee75f17c01a7cc42f958dc650907174af0554"),
}
for name, (repo, revision) in MODELS.items():
    print(f"Downloading: {repo}", flush=True)
    snapshot_download(repo_id=repo, revision=revision, local_dir=str(ROOT / "models" / name),
                      ignore_patterns=["*.md", "*.gitattributes", "*.msgpack", "*.h5", "pytorch_model*.bin", "onnx/*"],
                      max_workers=4)
    print(f"Ready: {name}", flush=True)
