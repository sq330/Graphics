from ...shared.constants import TEST_PROJECTS_DIR, PATH_UNITY_REVISION, PATH_TEST_RESULTS, PATH_PLAYERS, UTR_INSTALL_URL, UNITY_DOWNLOADER_CLI_URL, get_unity_downloader_cli_cmd
from ruamel.yaml.scalarstring import PreservedScalarString as pss
from ...shared.utr_utils import utr_editmode_flags, utr_playmode_flags, utr_standalone_split_flags,utr_standalone_not_split_flags, utr_standalone_build_flags, extract_flags


def _cmd_base(project_folder, platform, editor):
    return []

def cmd_editmode(project_folder, platform, api, test_platform, editor, build_config, color_space):
    scripting_backend = build_config["scripting_backend"]
    api_level = build_config["api_level"]
    utr_args = utr_standalone_build_flags(platform_spec='', platform='iOS', testproject=f'{TEST_PROJECTS_DIR}/{project_folder}', player_save_path=PATH_PLAYERS, scripting_backend=f'{scripting_backend}', api_level=f'{api_level}', color_space=f'{color_space}')
    utr_args.extend(extract_flags(test_platform["extra_utr_flags"], platform["name"], api["name"]))


    return [
        f'pip install unity-downloader-cli --index-url {UNITY_DOWNLOADER_CLI_URL} --upgrade',
        f'unity-downloader-cli { get_unity_downloader_cli_cmd(editor, platform["os"]) } {"".join([f"-c {c} " for c in platform["components"]])}  --wait --published-only',
        f'curl -s {UTR_INSTALL_URL} --output utr',
        f'chmod +x ./utr',
        f'./utr {" ".join(utr_args)}'
     ]

def cmd_playmode(project_folder, platform, api, test_platform, editor, build_config, color_space):
    scripting_backend = build_config["scripting_backend"]
    api_level = build_config["api_level"]
    utr_args = utr_playmode_flags(testproject=f'{TEST_PROJECTS_DIR}/{project_folder}', scripting_backend=f'{scripting_backend}', api_level=f'{api_level}', color_space=f'{color_space}')
    utr_args.extend(extract_flags(test_platform["extra_utr_flags"], platform["name"], api["name"]))


    return [
        f'pip install unity-downloader-cli --index-url {UNITY_DOWNLOADER_CLI_URL} --upgrade',
        f'unity-downloader-cli { get_unity_downloader_cli_cmd(editor, platform["os"]) } {"".join([f"-c {c} " for c in platform["components"]])}  --wait --published-only',
        f'curl -s {UTR_INSTALL_URL} --output utr',
        f'chmod +x ./utr',
        f'./utr {" ".join(utr_args)}'
     ]

def cmd_standalone(project_folder, platform, api, test_platform, editor, build_config, color_space):
    scripting_backend = build_config["scripting_backend"]
    api_level = build_config["api_level"]
    utr_args = utr_standalone_split_flags(platform_spec='', platform='iOS', player_load_path='players',player_conn_ip=None, scripting_backend=f'{scripting_backend}', api_level=f'{api_level}', color_space=f'{color_space}')
    utr_args.extend(extract_flags(test_platform["extra_utr_flags"], platform["name"], api["name"]))


    return [
        f'curl -s {UTR_INSTALL_URL} --output utr',        
        f'chmod +x ./utr',
        f'./utr {" ".join(utr_args)}'
    ]

        
def cmd_standalone_build(project_folder, platform, api, test_platform, editor, build_config, color_space):
    scripting_backend = build_config["scripting_backend"]
    api_level = build_config["api_level"]
    utr_args = utr_standalone_build_flags(platform_spec='', platform='iOS', testproject=f'{TEST_PROJECTS_DIR}/{project_folder}', player_save_path=PATH_PLAYERS, scripting_backend=f'{scripting_backend}', api_level=f'{api_level}', color_space=f'{color_space}')
    utr_args.extend(extract_flags(test_platform["extra_utr_flags_build"], platform["name"], api["name"]))


    return [
        f'pip install unity-downloader-cli --index-url {UNITY_DOWNLOADER_CLI_URL} --upgrade',
        f'unity-downloader-cli { get_unity_downloader_cli_cmd(editor, platform["os"]) } {"".join([f"-c {c} " for c in platform["components"]])}  --wait --published-only',
        f'curl -s {UTR_INSTALL_URL} --output utr',
        f'chmod +x ./utr',
        f'./utr {" ".join(utr_args)}'
     ]
