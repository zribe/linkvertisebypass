import json
import sys

from linkvertisebypass import bypass

print(json.dumps(bypass(sys.argv[1]).to_dict(), indent=2))
