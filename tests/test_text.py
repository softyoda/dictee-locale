"""Offline pure-text regressions. No third-party packages, models or recordings."""
import json
from pathlib import Path
import sys
import unittest
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'backend'))
from server import parse_cleanup, parse_edits, plain_text, spoken_punctuation

class TextTests(unittest.TestCase):
    def test_whitespace(self):
        self.assertEqual(plain_text(' a\n  b '), 'a b')
    def test_invalid_cleanup_keeps_original(self):
        for generated in ['not json', '{"text":""}', '{"unexpected":"text"}']:
            self.assertEqual(parse_cleanup(generated, 'original'), ('original', False))
    def test_question_commands(self):
        for raw in ["Tu viens point d'interrogation.", "Tu viens point d\u2019interrogation ?", 'Tu viens point d interrogation']:
            self.assertEqual(spoken_punctuation(raw), 'Tu viens ?')
        self.assertEqual(spoken_punctuation('Ready question mark'), 'Ready ?')
    def test_quoted_question_command_is_literal(self):
        for raw in ['Le nom "point d\u2019interrogation" est cite.', '`question mark`', '\u00abpoint d\u2019interrogation\u00bb']:
            self.assertEqual(spoken_punctuation(raw), raw)
    def test_ambiguous_absent_overlapping_edits_rejected(self):
        for original, edits in [('keep original',[{'old':'absent','new':'invented'}]),('same same',[{'old':'same','new':'different'}]),('abcd',[{'old':'abc','new':'a'},{'old':'bcd','new':'b'}])]:
            self.assertEqual(parse_edits(json.dumps({'edits':edits}), original), (original, False))
    def test_local_edits_preserve_neighbours(self):
        original='On se retrouve mardi, ah non attends mercredi. Keep the branch develop.'
        result,valid=parse_edits(json.dumps({'edits':[{'old':'mardi, ah non attends mercredi','new':'mercredi'}]}),original)
        self.assertTrue(valid)
        self.assertEqual(result,'On se retrouve mercredi. Keep the branch develop.')
    def test_excessive_loss_rejected(self):
        original='Keep this entire paragraph with all of its important facts and technical names.'
        self.assertEqual(parse_cleanup('{"text":"banane"}',original),(original,False))

if __name__=='__main__':
    unittest.main()
