using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Linq;
using System.Security.Cryptography;
using System.Web;
using ImmutableTrie;
using Libplanet.Common;
using Libplanet.Crypto;
using Libplanet.Store.Trie;
using Libplanet.Types.Blocks;
using Libplanet.Types.Tx;

namespace Libplanet.Store
{
    /// <summary>
    /// Volatile in-memory store.
    /// <para>It is useful for storing temporal small chains, e.g., fixtures for unit tests of
    /// game logic.</para>
    /// <para><see cref="MemoryStore"/> and <see cref="MemoryKeyValueStore"/>-backed
    /// <see cref="TrieStateStore"/> can be instantiated from a URI with <c>memory:</c> scheme
    /// using <see cref="StoreLoaderAttribute.LoadStore(Uri)"/>, e.g.:</para>
    /// <list type="bullet">
    /// <item><description><c>memory:</c></description></item>
    /// </list>
    /// </summary>
    public sealed class MemoryStore : IStore
    {
        private readonly ConcurrentDictionary<Guid, ImmutableTrieList<BlockHash>> _indices =
            new ConcurrentDictionary<Guid, ImmutableTrieList<BlockHash>>();

        private readonly ConcurrentDictionary<BlockHash, BlockDigest> _blocks =
            new ConcurrentDictionary<BlockHash, BlockDigest>();

        private readonly ConcurrentDictionary<BlockHash, DateTimeOffset> _blockPerceivedTimes =
            new ConcurrentDictionary<BlockHash, DateTimeOffset>();

        private readonly ConcurrentDictionary<TxId, Transaction> _txs =
            new ConcurrentDictionary<TxId, Transaction>();

        private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Address, long>> _txNonces =
            new ConcurrentDictionary<Guid, ConcurrentDictionary<Address, long>>();

        private readonly ConcurrentDictionary<(BlockHash, TxId), TxExecution> _txExecutions =
            new ConcurrentDictionary<(BlockHash, TxId), TxExecution>();

        private readonly ConcurrentDictionary<TxId, ImmutableHashSet<BlockHash>> _txBlockIndices =
            new ConcurrentDictionary<TxId, ImmutableHashSet<BlockHash>>();

        private readonly ConcurrentDictionary<BlockHash, BlockCommit> _blockCommits =
            new ConcurrentDictionary<BlockHash, BlockCommit>();

        private readonly ConcurrentDictionary<BlockHash, HashDigest<SHA256>> _nextStateRootHashes =
            new ConcurrentDictionary<BlockHash, HashDigest<SHA256>>();

        private readonly ConcurrentDictionary<Guid, BlockCommit> _chainCommits =
            new ConcurrentDictionary<Guid, BlockCommit>();

        private Guid? _canonicalChainId;

        void IDisposable.Dispose()
        {
            // Method intentionally left empty.
        }

        IEnumerable<Guid> IStore.ListChainIds() =>
            _indices.Keys;

        void IStore.DeleteChainId(Guid chainId)
        {
            _indices.TryRemove(chainId, out _);
            _txNonces.TryRemove(chainId, out _);
        }

        Guid? IStore.GetCanonicalChainId() =>
            _canonicalChainId;

        void IStore.SetCanonicalChainId(Guid chainId) =>
            _canonicalChainId = chainId;

        long IStore.CountIndex(Guid chainId) =>
            _indices.TryGetValue(chainId, out ImmutableTrieList<BlockHash>? index)
            ? index.Count
            : 0;

        IEnumerable<BlockHash> IStore.IterateIndexes(Guid chainId, int offset, int? limit)
        {
            if (_indices.TryGetValue(chainId, out var list))
            {
                IEnumerable<BlockHash> index = list.Skip(offset);
                return limit is { } l ? index.Take(l) : index;
            }

            return Enumerable.Empty<BlockHash>();
        }

        BlockHash? IStore.IndexBlockHash(Guid chainId, long index)
        {
            if (_indices.TryGetValue(chainId, out var list))
            {
                if (index < 0)
                {
                    index += list.Count;
                }

                if (index < list.Count && index >= 0)
                {
                    return list[(int)index];
                }
            }

            return null;
        }

        long IStore.AppendIndex(Guid chainId, BlockHash hash)
        {
            ImmutableTrieList<BlockHash> list = _indices.AddOrUpdate(
                chainId,
                _ => ImmutableTrieList.Create(hash),
                (_, list) => list.Add(hash)
            );
            _txNonces.GetOrAdd(chainId, _ => new ConcurrentDictionary<Address, long>());

            return list.Count - 1;
        }

        public void ForkBlockIndexes(
            Guid sourceChainId,
            Guid destinationChainId,
            BlockHash branchpoint
        )
        {
            if (_indices.TryGetValue(sourceChainId, out ImmutableTrieList<BlockHash>? source))
            {
                int bpIndex = source.FindIndex(branchpoint.Equals);
                _indices[destinationChainId] = source.GetRange(0, bpIndex + 1);
            }
        }

        Transaction? IStore.GetTransaction(TxId txid) =>
            _txs.TryGetValue(txid, out Transaction? untyped) && untyped is Transaction tx
                ? tx
                : null;

        void IStore.PutTransaction(Transaction tx) =>
            _txs[tx.Id] = tx;

        IEnumerable<BlockHash> IStore.IterateBlockHashes() =>
            _blocks.Keys;

        Block? IStore.GetBlock(BlockHash blockHash)
        {
            if (!_blocks.TryGetValue(blockHash, out BlockDigest digest))
            {
                return null;
            }

            BlockHeader header = digest.GetHeader();
            ImmutableArray<TxId> txids = digest.TxIds
                .Select(b => new TxId(b.ToBuilder().ToArray()))
                .ToImmutableArray();
            IEnumerable<Transaction> txs = txids.Select(txid => _txs[txid]);
            return new Block(header, txs);
        }

        long? IStore.GetBlockIndex(BlockHash blockHash) =>
            _blocks.TryGetValue(blockHash, out BlockDigest digest) ? digest.Index : (long?)null;

        BlockDigest? IStore.GetBlockDigest(BlockHash blockHash) =>
            _blocks.TryGetValue(blockHash, out BlockDigest digest) ? digest : (BlockDigest?)null;

        void IStore.PutBlock(Block block)
        {
            IReadOnlyList<Transaction> txs = block.Transactions;
            foreach (Transaction tx in txs)
            {
                _txs[tx.Id] = tx;
            }

            _blocks[block.Hash] = new BlockDigest(
                block.Header,
                txs.Select(tx => tx.Id.ByteArray).ToImmutableArray()
            );
        }

        bool IStore.DeleteBlock(BlockHash blockHash) =>
            _blocks.TryRemove(blockHash, out _);

        bool IStore.ContainsBlock(BlockHash blockHash) =>
            _blocks.ContainsKey(blockHash);

        void IStore.PutTxExecution(TxExecution txExecution) =>
            _txExecutions[(txExecution.BlockHash, txExecution.TxId)] = txExecution;

        TxExecution? IStore.GetTxExecution(BlockHash blockHash, TxId txid) =>
            _txExecutions.TryGetValue((blockHash, txid), out TxExecution? e) ? e : null;

        void IStore.PutTxIdBlockHashIndex(TxId txId, BlockHash blockHash) =>
            _txBlockIndices.AddOrUpdate(
                txId,
                _ => ImmutableHashSet.Create(blockHash),
                (_, set) => set.Add(blockHash)
            );

        BlockHash? IStore.GetFirstTxIdBlockHashIndex(TxId txId) =>
            _txBlockIndices.TryGetValue(txId, out ImmutableHashSet<BlockHash>? set) && set.Any()
                ? set.First()
                : (BlockHash?)null;

        IEnumerable<BlockHash> IStore.IterateTxIdBlockHashIndex(TxId txId) =>
            _txBlockIndices.TryGetValue(txId, out ImmutableHashSet<BlockHash>? set)
                ? set
                : Enumerable.Empty<BlockHash>();

        void IStore.DeleteTxIdBlockHashIndex(TxId txId, BlockHash blockHash)
        {
            while (_txBlockIndices.TryGetValue(txId, out ImmutableHashSet<BlockHash>? set) &&
                   set.Contains(blockHash))
            {
                var removed = set.Remove(blockHash);
                _txBlockIndices.TryUpdate(txId, removed, set);
            }
        }

        IEnumerable<KeyValuePair<Address, long>> IStore.ListTxNonces(Guid chainId) =>
            _txNonces.TryGetValue(chainId, out ConcurrentDictionary<Address, long>? dict)
                ? dict
                : Enumerable.Empty<KeyValuePair<Address, long>>();

        long IStore.GetTxNonce(Guid chainId, Address address) =>
            _txNonces.TryGetValue(chainId, out ConcurrentDictionary<Address, long>? dict) &&
            dict.TryGetValue(address, out long nonce)
                ? nonce
                : 0;

        void IStore.IncreaseTxNonce(Guid chainId, Address signer, long delta)
        {
            ConcurrentDictionary<Address, long> dict =
                _txNonces.GetOrAdd(chainId, _ => new ConcurrentDictionary<Address, long>());
            dict.AddOrUpdate(signer, _ => delta, (_, nonce) => nonce + delta);
        }

        bool IStore.ContainsTransaction(TxId txId) =>
            _txs.ContainsKey(txId);

        long IStore.CountBlocks() =>
            _blocks.Count;

        void IStore.ForkTxNonces(Guid sourceChainId, Guid destinationChainId)
        {
            if (_txNonces.TryGetValue(sourceChainId, out ConcurrentDictionary<Address, long>? dict))
            {
                _txNonces[destinationChainId] = new ConcurrentDictionary<Address, long>(dict);
            }
        }

        void IStore.PruneOutdatedChains(bool noopWithoutCanon)
        {
            if (!(_canonicalChainId is { } ccid))
            {
                if (noopWithoutCanon)
                {
                    return;
                }

                throw new InvalidOperationException("Canonical chain ID is not assigned.");
            }

            foreach (Guid id in _indices.Keys.Where(id => !id.Equals(ccid)))
            {
                ((IStore)this).DeleteChainId(id);
            }
        }

        /// <inheritdoc />
        public BlockCommit? GetChainBlockCommit(Guid chainId) =>
            _chainCommits.TryGetValue(chainId, out BlockCommit? commit)
                ? commit
                : null;

        /// <inheritdoc />
        public void PutChainBlockCommit(Guid chainId, BlockCommit blockCommit) =>
            _chainCommits[chainId] = blockCommit;

        public BlockCommit? GetBlockCommit(BlockHash blockHash) =>
            _blockCommits.TryGetValue(blockHash, out var commit)
                ? commit
                : null;

        /// <inheritdoc />
        public void PutBlockCommit(BlockCommit blockCommit) =>
            _blockCommits[blockCommit.BlockHash] = blockCommit;

        /// <inheritdoc />
        public void DeleteBlockCommit(BlockHash blockHash) =>
            _blockCommits.TryRemove(blockHash, out _);

        /// <inheritdoc />
        public IEnumerable<BlockHash> GetBlockCommitHashes()
            => _blockCommits.Keys;

        /// <inheritdoc />
        public HashDigest<SHA256>? GetNextStateRootHash(BlockHash blockHash)
            => _nextStateRootHashes.TryGetValue(blockHash, out var nextStateRootHash)
                ? (HashDigest<SHA256>?)nextStateRootHash
                : null;

        /// <inheritdoc />
        public void PutNextStateRootHash(BlockHash blockHash, HashDigest<SHA256> nextStateRootHash)
            => _nextStateRootHashes[blockHash] = nextStateRootHash;

        /// <inheritdoc />
        public void DeleteNextStateRootHash(BlockHash blockHash)
            => _nextStateRootHashes.TryRemove(blockHash, out _);

        [StoreLoader("memory")]
        private static (IStore Store, IStateStore StateStore) Loader(Uri storeUri)
        {
            NameValueCollection query = HttpUtility.ParseQueryString(storeUri.Query);
            var store = new MemoryStore();
            var stateStore = new TrieStateStore(new MemoryKeyValueStore());
            return (store, stateStore);
        }
    }
}
