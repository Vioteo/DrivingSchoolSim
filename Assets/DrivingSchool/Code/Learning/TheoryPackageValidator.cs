using System;
using System.IO;
using System.Collections.Generic;
using DrivingSchool.Contracts;

namespace DrivingSchool.Learning
{
    public static class TheoryPackageValidator
    {
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }

        public static void Validate(TheoryContentPack pack)
        {
            Require(pack != null, "Theory pack cannot be null");
            Require(pack.schemaVersion == 1, "Unsupported schemaVersion");
            Require(!string.IsNullOrWhiteSpace(pack.id), "Empty theory pack id");
            Require(!string.IsNullOrWhiteSpace(pack.revision), "Empty theory pack revision");
            Require(!string.IsNullOrWhiteSpace(pack.source), "Empty theory pack source");
            Require(pack.questions != null && pack.questions.Length > 0, "Theory pack has no questions");

            if (pack.isOfficial)
            {
                Require(pack.source.Contains("Официальный") || pack.source.Contains("ГИБДД") || pack.source.Contains("Verified"),
                    "Official status requires verified authority source");
            }

            var questionIds = new HashSet<string>();
            foreach (var q in pack.questions)
            {
                Require(q != null, "Question cannot be null");
                Require(!string.IsNullOrWhiteSpace(q.id) && questionIds.Add(q.id), $"Duplicate or empty question id: {q.id}");
                Require(!string.IsNullOrWhiteSpace(q.text), $"Empty question text in {q.id}");
                Require(!string.IsNullOrWhiteSpace(q.explanation), $"Empty explanation in {q.id}");
                Require(q.answers != null && q.answers.Length >= 2 && q.answers.Length <= 5, $"Question {q.id} must have 2 to 5 answers");

                foreach (var ans in q.answers)
                {
                    Require(!string.IsNullOrWhiteSpace(ans), $"Question {q.id} has empty answer text");
                }

                Require(q.correctIndex >= 0 && q.correctIndex < q.answers.Length,
                    $"Question {q.id} correctIndex {q.correctIndex} out of bounds [0..{q.answers.Length - 1}]");
            }
        }
    }
}
