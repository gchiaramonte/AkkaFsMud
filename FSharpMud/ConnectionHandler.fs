﻿module ConnectionHandler
open Akka
open Akka.Actor
open Akka.IO
open Akka.FSharp
open System.Net
open Thing
open Messages

let connectionHandler (startRoom:IActorRef) (remote:EndPoint) (connection:IActorRef) (mailbox : Actor<obj>) = 
    mailbox.Context.Watch connection |> ignore
    let player = spawn mailbox.Context.System null (thing "player")
    player <! SetOutput(mailbox.Self)
    player <! SetContainerByActorRef(startRoom)
    let rec loop() = 
        actor { 
            let! message = mailbox.Receive()
            match message with
            | :? Message as msg -> 
                match msg with
                | Message(format,args) ->
                    let string = System.String.Format(format,args |> List.toArray)
                    let bytes = System.Text.Encoding.ASCII.GetBytes(string)                    
                    mailbox.Sender() <! (Tcp.Write.Create(ByteString.Create(bytes)));
            | :? Tcp.Received as received -> 
                let text = System.Text.Encoding.UTF8.GetString(received.Data.ToArray()).Trim();
                printfn "Received %A" text
            | :? Tcp.ConnectionClosed -> 
                printfn "Stopped, remote connection [%A] closed" remote
                mailbox.Context.Stop mailbox.Self
            | :? Terminated -> 
                printfn "Stopped, remote connection [%A] died" remote
                mailbox.Context.Stop mailbox.Self
            | _ -> ()
            return! loop()
        }
    loop()

let mudService (startRoom:IActorRef) (endpoint:IPEndPoint) (mailbox : Actor<obj>) = 
    let manager = mailbox.Context.System.Tcp()
    manager <! (new Tcp.Bind(mailbox.Self, endpoint));
    let rec loop() = 
        actor { 
            let! message = mailbox.Receive()
            match message with
            | :? Tcp.Connected as connected -> 
                printfn "Remote address %A connected" connected.RemoteAddress;
                let handler = spawn mailbox.Context.System null (connectionHandler startRoom (connected.RemoteAddress) (mailbox.Sender()))
                mailbox.Sender() <! new Tcp.Register(handler)
            | _ -> ()
            return! loop()
        }
    loop()