from pathlib import Path

source_path = Path('.github/scripts/apply_ui42.py')
source = source_path.read_text(encoding='utf-8')
start = source.index('# CI coverage for feature branch and version assertions.')
end = source.index('# Documentation/version bookkeeping.')
filtered = source[:start] + source[end:]
exec(compile(filtered, str(source_path), 'exec'), {'__name__': '__main__'})
