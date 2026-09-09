"""Allow publication recovery only from the original successful signing job."""
import json
import os
import re
import subprocess


def validate(tag, run, tag_ref, jobs, artifacts):
    if not re.fullmatch(r'v\d+\.\d+\.\d+\.\d+', tag):
        raise RuntimeError('An exact version tag is required')
    if (run['path'] != '.github/workflows/publish-release.yml'
            or run['event'] != 'workflow_dispatch'
            or run['head_branch'] != 'main'
            or run['status'] != 'completed'
            or run['head_sha'] != tag_ref['object']['sha']):
        raise RuntimeError('Run is not the approved release for this exact tag')
    signed = [j for j in jobs if j['name'] == 'sign / build-sign-verify']
    if len(signed) != 1 or signed[0]['conclusion'] != 'success':
        raise RuntimeError('Original signing and verification did not succeed')
    matching = [a for a in artifacts if a['name'] == f'PitMedic-{tag}-signed-release']
    if len(matching) != 1 or matching[0]['expired']:
        raise RuntimeError('Exactly one retained signed artifact is required')


def main():
    repo = os.environ['GITHUB_REPOSITORY']
    tag = os.environ['RELEASE_TAG']
    run_id = os.environ['SIGNED_RUN_ID']
    if not run_id.isdigit() or not re.fullmatch(r'v\d+\.\d+\.\d+\.\d+', tag):
        raise RuntimeError('Invalid recovery inputs')
    def api(path):
        return json.loads(subprocess.check_output(
            ['gh', 'api', f'repos/{repo}/{path}'], text=True, encoding='utf-8'))
    run = api(f'actions/runs/{run_id}')
    tag_ref = api(f'git/ref/tags/{tag}')
    jobs = api(f'actions/runs/{run_id}/jobs?filter=all&per_page=100')['jobs']
    # The successful signing job may be from an earlier attempt than publication.
    jobs = sorted(jobs, key=lambda j: j['id'], reverse=True)
    latest = {}
    for job in jobs:
        latest.setdefault(job['name'], job)
    artifacts = api(f'actions/runs/{run_id}/artifacts?per_page=100')['artifacts']
    validate(tag, run, tag_ref, list(latest.values()), artifacts)
    print(f'Validated original signed artifact for {tag} from run {run_id}')


if __name__ == '__main__':
    main()
