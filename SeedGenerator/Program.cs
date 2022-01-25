using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Client;
using PatientManagement.AdmissionDischargeTransfer.Commands;
using PatientManagement.Framework;
using PatientManagement.Framework.Commands;
using SeedGenerator;

var ct = new CancellationTokenSource().Token;

var random = new Random();
var listOfPatients = Patient.Generate(400);

var dispatcher = SetupDispatcher();

await AdmitPatients(listOfPatients, dispatcher, ct);
await TransferPatients(listOfPatients, dispatcher, ct);
await DischargeAllPatients(listOfPatients, dispatcher, ct);

listOfPatients = Patient.Generate(20);

await AdmitPatients(listOfPatients, dispatcher, ct);
await TransferPatients(listOfPatients, dispatcher, ct);
await DischargePatients(listOfPatients, dispatcher, ct);

async Task DischargePatients(IEnumerable<Patient> listOfPatients, Dispatcher dispatcher, CancellationToken ct)
{
    foreach (var patient in listOfPatients)
    {
        var discharge = random.Next(0, 5) == 0;
        if (discharge)
        {
            await dispatcher.Dispatch(new DischargePatient(patient.Id), ct);
        }

        Console.WriteLine("Discharging: " + patient.Name);
    }
}

async Task DischargeAllPatients(IEnumerable<Patient> listOfPatients, Dispatcher dispatcher, CancellationToken ct)
{
    foreach (var patient in listOfPatients)
    {
        await dispatcher.Dispatch(new DischargePatient(patient.Id), ct);

        Console.WriteLine("Discharging: " + patient.Name);
    }
}

async Task TransferPatients(List<Patient> listOfPatients, Dispatcher dispatcher, CancellationToken ct)
{
    for (var i = 0; i < 10; i++)
    {
        foreach (var patient in listOfPatients)
        {
            var transfer = random.Next(0, 5) == 0;
            if (transfer)
            {
                await dispatcher.Dispatch(new TransferPatient(patient.Id, Ward.Get()), ct);
                Console.WriteLine("Transfering: " + patient.Name);
            }
        }
    }
}

async Task AdmitPatients(IEnumerable<Patient> listOfPatients, Dispatcher dispatcher, CancellationToken ct)
{
    foreach (var patient in listOfPatients)
    {
        await dispatcher.Dispatch(new AdmitPatient(
            patient.Id,
            patient.Name,
            patient.Age,
            DateTime.UtcNow,
            Ward.Get()), 
            ct);
        Console.WriteLine("Admitting: " + patient.Name);
    }
}

Dispatcher SetupDispatcher()
{
    var repository = new AggregateRepository(GetEventStore());

    var commandHandlerMap = new CommandHandlerMap(new Handlers(repository));

    return new Dispatcher(commandHandlerMap);
}

EventStoreClient GetEventStore()
{
    const string connectionString = 
        "esdb://localhost:2113?tls=false";
    return new EventStoreClient(EventStoreClientSettings.Create(connectionString));
}