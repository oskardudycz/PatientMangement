using System;
using System.Threading;
using EventStore.Client;
using PatientManagement.AdmissionDischargeTransfer.Commands;
using PatientManagement.Framework;
using PatientManagement.Framework.Commands;

var ct = new CancellationTokenSource().Token;

var dispatcher = SetupDispatcher();

var patientId = Guid.NewGuid();

var admitPatient = new AdmitPatient(patientId, "Tony Ferguson", 32, DateTime.UtcNow, 10);
await dispatcher.Dispatch(admitPatient, ct);

var transferPatientOne = new TransferPatient(patientId, 76);
await dispatcher.Dispatch(transferPatientOne, ct);

var transferPatientTwo = new TransferPatient(patientId, 34);
await dispatcher.Dispatch(transferPatientTwo, ct);

var dischargePatient = new DischargePatient(patientId);
await dispatcher.Dispatch(dischargePatient, ct);


Console.ReadLine();

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