"""Regression coverage for immutable supplemental implementation evidence."""
import importlib.util
import pathlib
import unittest

MODULE_PATH = pathlib.Path(__file__).resolve().parents[2] / 'build/game-ui/Validate-UiTasks.py'
SPEC = importlib.util.spec_from_file_location('ui_tasks_validator', MODULE_PATH)
VALIDATOR = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VALIDATOR)


def require(name, condition):
    if not condition:
        raise ValueError(name)


def snapshot(path, digest):
    return dict(path=path, sha256=digest)


def change(path, before, after):
    return dict(path=path, beforeSha256=before, afterSha256=after)


def advance(current, changes=(), snapshots=()):
    return VALIDATOR.advance_source_history(current, dict(sourceChanges=list(changes), sourceHashes=list(snapshots)), require)


class SourceHistoryTests(unittest.TestCase):
    def test_valid_history_does_not_rewrite_earlier_report(self):
        foundation = {'client.csproj': 'a' * 64}
        older = advance(foundation, [change('client.csproj', 'a' * 64, 'b' * 64)],
                        [snapshot('client.csproj', 'b' * 64), snapshot('new.cs', 'c' * 64)])
        newer = advance(older, [change('client.csproj', 'b' * 64, 'd' * 64)],
                        [snapshot('client.csproj', 'd' * 64), snapshot('new.cs', 'c' * 64)])
        self.assertEqual(foundation, {'client.csproj': 'a' * 64})
        self.assertEqual(older['client.csproj'], 'b' * 64)
        self.assertEqual(newer, {'client.csproj': 'd' * 64, 'new.cs': 'c' * 64})

    def test_missing_transition_rejected(self):
        with self.assertRaisesRegex(ValueError, 'supplementalSnapshotMatchesHistory'):
            advance({'client.csproj': 'a'}, snapshots=[snapshot('client.csproj', 'b')])

    def test_wrong_before_rejected(self):
        with self.assertRaisesRegex(ValueError, 'supplementalSourceTransitionMatches'):
            advance({'client.csproj': 'a'}, [change('client.csproj', 'wrong', 'b')], [snapshot('client.csproj', 'b')])

    def test_unknown_transition_rejected(self):
        with self.assertRaisesRegex(ValueError, 'supplementalSourceTransitionMatches'):
            advance({}, [change('new.cs', None, 'b')], [snapshot('new.cs', 'b')])

    def test_transition_requires_matching_snapshot(self):
        for snapshots in ([], [snapshot('a', 'wrong')]):
            with self.subTest(snapshots=snapshots), self.assertRaisesRegex(ValueError, 'supplementalTransitionHasSnapshot'):
                advance({'a': 'old'}, [change('a', 'old', 'new')], snapshots)

    def test_new_snapshot_tracked_and_cannot_silently_change(self):
        tracked = advance({}, snapshots=[snapshot('new.cs', 'old')])
        with self.assertRaisesRegex(ValueError, 'supplementalSnapshotMatchesHistory'):
            advance(tracked, snapshots=[snapshot('new.cs', 'new')])

    def test_duplicate_entries_rejected(self):
        with self.assertRaisesRegex(ValueError, 'uniqueSupplementalSourceChanges'):
            advance({'a': 'old'}, [change('a', 'old', 'middle'), change('a', 'middle', 'new')], [snapshot('a', 'new')])
        with self.assertRaisesRegex(ValueError, 'uniqueSupplementalSourceHashes'):
            advance({}, snapshots=[snapshot('a', 'old'), snapshot('a', 'new')])


if __name__ == '__main__':
    unittest.main(verbosity=2)
