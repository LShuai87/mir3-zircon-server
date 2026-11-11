#!/usr/bin/env python3
"""Verify reference reproduction and fail-closed provenance guards; no live game access."""
import argparse
import importlib.util
import json
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('reference_map', HERE/'Build-ReferenceMap.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--inspection', type=Path, required=True)
    parser.add_argument('--art', type=Path, required=True)
    parser.add_argument('--repeat', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    module.require(not args.output.exists(), 'Verification output must be new')
    checks = []
    def check(label, condition):
        module.require(condition, label)
        checks.append(dict(id=label,result='pass'))
    check('matching-repeat-identical', module.sha(HERE/'panel-matching.json') == module.sha(args.repeat/'panel-matching-repeat.json'))
    saved, repeat = HERE/'generated', args.repeat/'generated-repeat'
    names = sorted(p.name for p in saved.iterdir())
    check('repeat-file-set', names == sorted(p.name for p in repeat.iterdir()) and len(names)==7)
    for name in names:
        check('repeat-identical/'+name, module.sha(saved/name)==module.sha(repeat/name))
    model = module.load(saved/'reference-map.json')
    check('five-active-originals', len(model['views'])==5 and all(v['captureId']!='06-chat-chinese-input' for v in model['views']))
    check('33-local-comparisons', len(model['observations'])==33)
    check('92-candidate-assets', len(model['assets'])==92)
    check('all-alpha-counts-valid', all(sum(a['alphaPixels'].values())==a['outputSize'][0]*a['outputSize'][1] and a['alphaPixels']['intermediate']==0 for a in model['assets']))
    check('reference-only-activation', model['runtimeReady'] is False and model['releaseAccepted'] is False)
    check('historical-source-plan-unchanged', module.sha(module.ROOT/'docs/design/official-ingame-ui-implementation-plan.md') == 'f8c0a002b3821fabb54993792c354c4eff35eec51f9cfdc418535017c13069e9')
    for v in model['views']:
        check('original-preserved/'+v['captureId'], module.sha(module.ROOT/v['originalPath'])==v['originalSha256'])
        check('annotation-preserved/'+v['captureId'], module.sha(saved/v['annotatedImage'])==v['annotatedSha256'])
    for item in model['inputs']:
        check('input-preserved/'+item['path'], module.sha(module.ROOT/item['path'])==item['sha256'])
    check('generator-preserved', module.sha(HERE/'Build-ReferenceMap.py')==model['generatorSha256'])
    check('default-legacy-retained', 'GameUiStyle { get; set; } = "Legacy";' in (module.ROOT/'Client/Envir/Config.cs').read_text(encoding='utf-8-sig'))
    def rejected(label, output, message, **overrides):
        try:
            module.build(overrides.get('inspection', args.inspection),overrides.get('art', args.art),output)
        except ValueError as ex:
            check(label, message in str(ex))
        else:
            raise ValueError('Invalid input was accepted: '+label)
    rejected('reject-existing-output', repeat, 'Output must not exist')
    with tempfile.TemporaryDirectory(prefix='reference-guards-',dir=args.repeat) as tmp:
        fixture = Path(tmp).resolve()
        check('fixture-inside-work', fixture.is_relative_to(args.repeat.resolve()))
        missing_output = fixture/'missing-art-output'
        rejected('reject-missing-art', missing_output, 'Candidate PNG hash mismatch', art=fixture/'missing-art')
        check('missing-art-no-partial-output', not missing_output.exists())
        original_here = module.HERE
        requests = module.load(HERE/'effective-matching-spec.json')
        matches = module.load(HERE/'panel-matching.json')
        try:
            module.HERE = fixture
            requests['matches'][0]['captureId']='06-chat-chinese-input'
            matches['matches'][0]['captureId']='06-chat-chinese-input'
            module.save(fixture/'effective-matching-spec.json',requests)
            matches['specSha256']=module.sha(fixture/'effective-matching-spec.json')
            module.save(fixture/'panel-matching.json',matches)
            ignored_output=fixture/'ignored-output'
            rejected('reject-ignored-screenshot',ignored_output,'Ignored screenshot cannot be a reference')
            check('ignored-no-partial-output',not ignored_output.exists())
            requests=module.load(HERE/'effective-matching-spec.json')
            matches=module.load(HERE/'panel-matching.json')
            matches['matches'][0]['captureSha256']='0'*64
            module.save(fixture/'effective-matching-spec.json',requests)
            matches['specSha256']=module.sha(fixture/'effective-matching-spec.json')
            module.save(fixture/'panel-matching.json',matches)
            bad_hash_output=fixture/'bad-hash-output'
            rejected('reject-wrong-capture-hash',bad_hash_output,'Original capture hash mismatch')
            check('wrong-hash-no-partial-output',not bad_hash_output.exists())
        finally:
            module.HERE=original_here
    result=dict(schemaVersion=1,kind='scoped-reference-reproduction-validation',result='pass',
                checkCount=len(checks),checks=checks,releaseAccepted=False,runtimeReady=False)
    module.save(args.output,result)
    print(json.dumps(dict(result='pass',checks=len(checks),reproducedFiles=8,negativeGuards=4,
                         liveGameAccess=False),ensure_ascii=False))


if __name__=='__main__':
    main()
