"""Conservative change-aware CI selection; unknown paths run everything."""
import json
import os
import subprocess


def scope(paths):
    result = dict(build=False, knowledge=False, worker=False, dashboard=False)
    for path in paths:
        if path.startswith(('Source/', 'Build/', 'Installer/', '.github/')) or path in ('global.json', '.gitattributes'):
            return dict.fromkeys(result, True)
        if path.startswith(('website/', 'Knowledge/', 'Tools/')):
            result['knowledge'] = True
        elif path.startswith('TelemetryWorker/'):
            result['worker'] = True
        elif path.startswith('TelemetryDashboard/'):
            result['dashboard'] = True
        elif path.endswith(('.md', '.txt')):
            pass
        else:
            return dict.fromkeys(result, True)
    return result


if __name__ == '__main__':
    event = json.load(open(os.environ['GITHUB_EVENT_PATH']))
    kind = os.environ['GITHUB_EVENT_NAME']
    if kind == 'workflow_dispatch' and os.environ.get('GITHUB_REF', '').startswith('refs/heads/release-metadata/'):
        files = subprocess.check_output(['git', 'diff', '--name-only', 'origin/main', 'HEAD'], text=True).splitlines()
        result = scope(files)
    elif kind in ('workflow_dispatch', 'workflow_call') or not event:
        result = scope(['Build/'])
    else:
        base = event['pull_request']['base']['sha'] if kind == 'pull_request' else event['before']
        head = event['pull_request']['head']['sha'] if kind == 'pull_request' else event['after']
        try:
            files = subprocess.check_output(['git', 'diff', '--name-only', base, head], text=True).splitlines()
            result = scope(files)
        except subprocess.CalledProcessError:
            result = scope(['Build/'])
    with open(os.environ['GITHUB_OUTPUT'], 'a') as output:
        for key, value in result.items():
            output.write(f'{key}={str(value).lower()}\n')
