"""Normalize unsigned development NuGet packaging metadata for stable lock-file hashes."""
import re
import sys
import zipfile
from pathlib import Path

package = Path(sys.argv[1])
temporary = package.with_suffix(".normalized.tmp")
try:
    with zipfile.ZipFile(package) as source, zipfile.ZipFile(temporary, "w") as output:
        for name in sorted(source.namelist()):
            data = source.read(name)
            if name.endswith(".rels"):
                # NuGet relationship IDs can vary between pack processes; they carry no identity semantics.
                sequence = iter(range(1, 1000))
                data = re.sub(rb'\bId="[^"]+"', lambda _: f'Id="rId{next(sequence)}"'.encode(), data)
            info = zipfile.ZipInfo(name, (2000, 1, 1, 0, 0, 0))
            info.create_system = 0
            info.external_attr = 0
            # Stored entries avoid zlib-version differences for this small local package.
            output.writestr(info, data, compress_type=zipfile.ZIP_STORED)
    temporary.replace(package)
finally:
    temporary.unlink(missing_ok=True)
