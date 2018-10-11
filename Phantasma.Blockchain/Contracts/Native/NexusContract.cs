﻿using Phantasma.Cryptography;
using Phantasma.Numerics;
using System.Collections.Generic;

namespace Phantasma.Blockchain.Contracts.Native
{
    public sealed class NexusContract : NativeContract
    {
        internal override ContractKind Kind => ContractKind.Nexus;

        private HashSet<Hash> knownTransactions = new HashSet<Hash>();

        public NexusContract() : base()
        {
        }

        public bool IsKnown(Hash hash)
        {
            return knownTransactions.Contains(hash);
        }

        public bool IsRootChain(Address address)
        {
            return (address == this.Chain.Address);
        }

        public bool IsSideChain(Address address)
        {
            return false;
        }

        public void CreateToken(string symbol, string name, BigInteger maxSupply)
        {
            throw new System.NotImplementedException();
        }

        public Chain CreateChain(Address owner, string name, string parentName)
        {
            Expect(!string.IsNullOrEmpty(name));
            Expect(!string.IsNullOrEmpty(parentName));

            Expect(IsWitness(owner));

            Expect(name != parentName);

            var parent = this.Nexus.FindChainByName(parentName);
            Expect(parent != null);

            var chain = this.Nexus.CreateChain(owner, name, parent, this.Block);
            Expect(chain != null);

            return chain;
        }

        public void SendToken(Address token, Address from, Address to, BigInteger amount)
        {
            Expect(IsWitness(from));

            if (IsRootChain(this.Chain.Address))
            {
                Expect(IsSideChain(to));
            }
            else
            {
                Expect(IsRootChain(to));
            }

            Expect(!IsKnown(Transaction.Hash));

            var fee = amount / 10;
            Expect(fee > 0);

            throw new System.NotImplementedException();
            /*
            var otherChain = this.Chain.FindChain(to);
            var otherConsensus = (ConsensusContract)otherChain.FindContract(ContractKind.Consensus);
            Expect(otherConsensus.IsValidReceiver(from));

            //var tokenContract = (TokenContract) this.Chain.FindContract(NativeContractKind.Token);
            var tokenContract = this.Chain.FindContract(token);
            Expect(tokenContract != null);

            var tokenABI = Chain.FindABI(NativeABI.Token);
            Expect(tokenContract.ABI.Implements(tokenABI));

            tokenContract.SetData(this.Chain, this.Transaction, this.Storage);

            tokenABI["Transfer"].Invoke(tokenContract, from, this.Address, amount);
            tokenABI["Burn"].Invoke(tokenContract, this.Address, amount);

            knownTransactions.Add(Transaction.Hash);*/
        }

        public void ReceiveToken(Address token, Address from, Address to, Hash hash)
        {
            if (IsRootChain(this.Chain.Address))
            {
                Expect(IsSideChain(from));
            }
            else
            {
                Expect(IsRootChain(from));
            }

            Expect(!IsKnown(hash));

            throw new System.NotImplementedException();
            /*
            var otherChain = this.Chain.FindChain(from);
            var otherNexus = (NexusContract) otherChain.FindContract(ContractKind.Nexus);
            Expect(otherNexus.IsKnown(hash));

            var tx = otherChain.FindTransaction(hash);
            BigInteger amount = null; // TODO obtain real amount from "tx"

            var tokenContract = this.Chain.FindContract(token);
            Expect(tokenContract != null);

            var tokenABI = Chain.FindABI(NativeABI.Token);
            Expect(tokenContract.ABI.Implements(tokenABI));

            tokenContract.SetData(this.Chain, this.Transaction, this.Storage);

            tokenABI["Mint"].Invoke(tokenContract, this.Address, amount);
            tokenABI["Transfer"].Invoke(tokenContract, this.Address, to, amount);

            knownTransactions.Add(Transaction.Hash);*/
        }

        public void MintToken(string symbol, Address target, BigInteger amount)
        {
            Expect(amount > 0);

            var token = this.Nexus.FindToken(symbol);
            Expect(token != null);

            Expect(IsWitness(token.Owner));
            Expect(token.Mint(target, amount));
        }

        public void BurnToken(string symbol, Address target, BigInteger amount)
        {           
            Expect(amount > 0);
            Expect(IsWitness(target));

            var token = this.Nexus.FindToken(symbol);
            Expect(token != null);

            Expect(token.Burn(target, amount));           
        }

        public void TransferToken(string symbol, Address source, Address destination, BigInteger amount)
        {
            Expect(amount > 0);
            Expect(source != destination);
            Expect(IsWitness(source));

            var token = this.Nexus.FindToken(symbol);
            Expect(token != null);

            Expect(token.Transfer(source, destination, amount));
        }

        public BigInteger GetBalance(string symbol, Address address)
        {
            var token = this.Nexus.FindToken(symbol);
            Expect(token != null);

            return token.GetBalance(address);
        }

    }
}
