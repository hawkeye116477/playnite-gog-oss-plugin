import os
import subprocess
import sys

pj = os.path.join
pn = os.path.normpath

script_path = os.path.dirname(os.path.realpath(__file__))
main_path = pn(script_path+"/..")
os.chdir(main_path)

home = os.path.expanduser("~")
PROTOC = pn(home+"/scoop/apps/protobuf/current/bin/protoc.exe")
cmd = [
    PROTOC,
    f"--proto_path={pj(main_path, "third_party", "gog_protocols")}",
    f"--csharp_out={pj(main_path, "third_party", "gog_protocols_generated")}",
    "gog.protocols.pb.proto",
    "galaxy.protocols.communication_service.proto"
]
subprocess.run(cmd, check=True)
