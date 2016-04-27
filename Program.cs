using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using NMF.Models.Repository;
using NMF.Utilities;
using TTC2016.ArchitectureCRA.ArchitectureCRA;

namespace ClassDiagramOptimization
{
    class Program
    {
        static void Main(string[] args)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            var repository = new ModelRepository();
            var model = repository.Resolve(args[0]);
            var classModel = model.RootElements[0] as ClassModel;
            stopwatch.Stop();
            Console.WriteLine("Loading model took {0}ms", stopwatch.ElapsedMilliseconds);

            stopwatch.Restart();
            foreach (var feature in classModel.Features)
            {
                var featureClass = new Class()
                {
                    Name = "C" + feature.Name
                };
                featureClass.Encapsulates.Add(feature);
                classModel.Classes.Add(featureClass);
            }

            var mai = new Func<IClass, IClass, double>((cl_i, cl_j) =>
                cl_i.Encapsulates.OfType<Method>()
                    .SelectMany(m => m.DataDependency)
                    .Intersect(cl_j.Encapsulates)
                    .Count());

            var mmi = new Func<IClass, IClass, double>((cl_i, cl_j) =>
                cl_i.Encapsulates.OfType<Method>()
                    .SelectMany(m => m.FunctionalDependency)
                    .Intersect(cl_j.Encapsulates)
                    .Count());

            var possibleMerges = from cl_i in classModel.Classes
                                 from cl_j in classModel.Classes
                                 where cl_i.Name.CompareTo(cl_j.Name) < 0
                                 select new
                                 {
                                     Cl_i = cl_i,
                                     Cl_j = cl_j,
                                     M_i = cl_i.Encapsulates.OfType<Method>().Count(),
                                     M_j = cl_j.Encapsulates.OfType<Method>().Count(),
                                     A_i = cl_i.Encapsulates.OfType<IAttribute>().Count(),
                                     A_j = cl_j.Encapsulates.OfType<IAttribute>().Count(),
                                     MAI_i = mai(cl_i, cl_i),
                                     MAI_j = mai(cl_j, cl_j),
                                     MAI_ij = mai(cl_i, cl_j),
                                     MAI_ji = mai(cl_j, cl_i),
                                     MMI_i = mmi(cl_i, cl_i),
                                     MMI_j = mmi(cl_j, cl_j),
                                     MMI_ij = mmi(cl_i, cl_j),
                                     MMI_ji = mmi(cl_j, cl_i)
                                 };

            var atLeastOne = new Func<int, int>(i => Math.Max(i, 1));
            var combinationCount = new Func<int, int>(i => Math.Max(i * i - 1, 1));

            var prioritizedMerges = possibleMerges.Select(m =>
                new
                {
                    Merge = m,
                    Effect =
                        // Delta of Cohesion based on data dependencies
                        (m.MAI_i + m.MAI_ij + m.MAI_ji + m.MAI_j) / atLeastOne((m.M_i + m.M_j) * (m.A_i + m.A_j)) - (m.MAI_i / atLeastOne(m.M_i * m.A_i)) - (m.MAI_j / atLeastOne(m.M_j * m.A_j))
                        +
                        // Delta of Cohesion based on functional dependencies
                        (m.MMI_i + m.MMI_ij + m.MMI_ji + m.MMI_j) / combinationCount((m.M_i + m.M_j)) - (m.MMI_i / combinationCount(m.M_i)) - (m.MMI_j / combinationCount(m.M_j))
                        +
                        // Delta of Coupling between C_i and C_j
                        (m.MAI_ij / atLeastOne(m.M_i * m.A_j)) + (m.MAI_ji / atLeastOne(m.M_j * m.A_i)) + (m.MMI_ij / atLeastOne(m.M_i * (m.M_j - 1))) + (m.MMI_ji / atLeastOne(m.M_j * (m.M_i - 1)))
                }).OrderByDescending(m => m.Effect);

            var nextMerge = prioritizedMerges.FirstOrDefault();
            var classCounter = 1;
            while (nextMerge != null && nextMerge.Effect > 0)
            {
                Console.WriteLine("Now merging {0} and {1}", nextMerge.Merge.Cl_i.Name, nextMerge.Merge.Cl_j.Name);
                // We need to save the features from these classes as they will be dropped as soon as we delete the encapsulating classes
                var newFeatures = nextMerge.Merge.Cl_i.Encapsulates.Concat(nextMerge.Merge.Cl_j.Encapsulates).ToList();
                classModel.Classes.Remove(nextMerge.Merge.Cl_i);
                classModel.Classes.Remove(nextMerge.Merge.Cl_j);
                var newClass = new Class() { Name = "C" + (classCounter++).ToString() };
                newClass.Encapsulates.AddRange(newFeatures);
                classModel.Classes.Add(newClass);
                nextMerge = prioritizedMerges.FirstOrDefault();
            }
            stopwatch.Stop();
            Console.WriteLine("Model optimization took {0}ms", stopwatch.ElapsedMilliseconds);

            stopwatch.Restart();
            classModel.Name = "Optimized Class Model";
            repository.Save(classModel, Path.ChangeExtension(args[0], ".Output.xmi"));
            stopwatch.Stop();

            Console.WriteLine("Serializing result model took {0}ms", stopwatch.ElapsedMilliseconds);
        }
    }
}
