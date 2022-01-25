using PatientManagement.Framework;
using PatientManagement.Framework.Commands;

namespace PatientManagement.AdmissionDischargeTransfer.Commands;

public class Handlers : CommandHandler
{
    public Handlers(AggregateRepository repository)
    {
        Register<AdmitPatient>(async (c, ct) =>
        {
            var encounter = new Encounter(c.PatientId, c.PatientName, c.AgeInYears, c.WardNumber);
            await repository.Save(encounter, ct);
        });

        Register<TransferPatient>(async (c, ct) =>
        {
            var encounter = await repository.Get<Encounter>(c.PatientId, ct);
            encounter.Transfer(c.WardNumber);
            await repository.Save(encounter, ct);
        });

        Register<DischargePatient>(async (c, ct) =>
        {
            var encounter = await repository.Get<Encounter>(c.PatientId, ct);
            encounter.DischargePatient();
            await repository.Save(encounter, ct);
        });
    }
}