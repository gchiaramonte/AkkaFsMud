﻿module ConnectionHandler
open Akka.Actor
open Akka.IO
open Akka.FSharp
open System.Net
open Thing
open Messages
open System.Text
open AnsiSupport
open InputHandler
open System.Collections.Generic

let receiveInput (sb:StringBuilder) (received:Tcp.Received)=
    let text = Encoding.UTF8.GetString(received.Data.ToArray());
    sb.Append(text) |> ignore

    let all = sb.ToString()
    let enter = all.IndexOf('\r')
    if enter >= 0 then
        let command = all.Substring(0,enter)
        sb.Remove(0,enter+2) |> ignore
        let stack = new Stack<char>()
        for c in command do
            if c = '\b' then stack.Pop() |> ignore
            else stack.Push c

        let cmd = new System.String(stack |> Seq.rev |> Seq.toArray)
        
        Some(cmd)
    else
        None

let write target (text:string) =
    let bytes = System.Text.Encoding.UTF8.GetBytes(text)  
    let byteString = ByteString.Create(bytes,0,bytes.Length)
    target <! (Tcp.Write.Create(byteString))

let loginHandler (startRoom:IActorRef) (remote:EndPoint) (connection:IActorRef) (mailbox : Actor<obj>) = 
    mailbox.Context.Watch connection |> ignore
   
    let inputBuffer = new StringBuilder()    
    write connection "Welcom to Akka FS MUD\r\n"
    write connection "Please enter your name\r\n"
    let rec play player = 
        actor { 
            let! message = mailbox.Receive()
            match message with
            | :? Message as msg -> 
                match msg with
                | Message(format,args) -> write connection (formatAnsi format args)
            | :? Tcp.Received as received ->                 
                match receiveInput inputBuffer received with 
                | Some(command) -> handleInput player command
                | None -> ()

            | :? Tcp.ConnectionClosed -> 
                printfn "Stopped, remote connection [%A] closed" remote
                mailbox.Context.Stop mailbox.Self
            | :? Terminated -> 
                printfn "Stopped, remote connection [%A] died" remote
                mailbox.Context.Stop mailbox.Self
            | _ -> ()
            return! play player
        }

    let rec login() = 
        actor { 
            let! message = mailbox.Receive()
            match message with         
            | :? Tcp.Received as received -> 
                match receiveInput inputBuffer received with 
                | Some(name) ->
                    write connection (toAnsi("You will be known as " + name.yellow+ "\r\n"))
                    let player = spawn mailbox.Context.System null (living name)
                    player <! SetOutput(mailbox.Self)
                    player <! SetContainerByActorRef(startRoom)
                    player <! Look
                    inputBuffer.Clear() |> ignore //TODO: this is ugly
                    return! play player
                | None -> ()

            | :? Tcp.ConnectionClosed -> 
                printfn "Stopped, remote connection [%A] closed" remote
                mailbox.Context.Stop mailbox.Self
            | :? Terminated -> 
                printfn "Stopped, remote connection [%A] died" remote
                mailbox.Context.Stop mailbox.Self
            | _ -> ()
            return! login()
        }
    login()

let mudService (startRoom:IActorRef) (endpoint:IPEndPoint) (mailbox : Actor<obj>) = 
    let manager = mailbox.Context.System.Tcp()
    manager <! (new Tcp.Bind(mailbox.Self, endpoint));
    let rec loop() = 
        actor { 
            let! message = mailbox.Receive()
            match message with
            | :? Tcp.Connected as connected -> 
                printfn "Remote address %A connected" connected.RemoteAddress;
                let handler = spawn mailbox.Context.System null (loginHandler startRoom (connected.RemoteAddress) (mailbox.Sender()))
                mailbox.Sender() <! new Tcp.Register(handler)
            | _ -> ()
            return! loop()
        }
    loop()