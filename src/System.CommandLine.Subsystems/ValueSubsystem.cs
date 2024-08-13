﻿// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine.Subsystems;
using System.CommandLine.Subsystems.Annotations;

namespace System.CommandLine;

public class ValueSubsystem(IAnnotationProvider? annotationProvider = null)
    : CliSubsystem(ValueAnnotations.Prefix, SubsystemKind.Value, annotationProvider)
{
    private Dictionary<CliSymbol, object?> cachedValues = [];
    private ParseResult? parseResult = null;

    // It is possible that another subsystems GetIsActivated method will access a value. 
    // If this is called from a GetIsActivated method of a subsystem in the early termination group, 
    // it will fail. That is not an expected scenario.
    /// <inheritdoc cref="CliSubsystem.GetIsActivated"/>
    /// <remarks>
    /// Note to inheritors: Call base for all ValueSubsystem methods that you override to ensure correct behavior
    /// </remarks>
    protected internal override bool GetIsActivated(ParseResult? parseResult)
    {
        this.parseResult = parseResult;
        return true;
    }

    /// <inheritdoc cref="CliSubsystem.Execute"/>
    /// <remarks>
    /// Note to inheritors: Call base for all ValueSubsystem methods that you override to ensure correct behavior
    /// </remarks>
    protected internal override void Execute(PipelineResult pipelineResult)
    {
        parseResult ??= pipelineResult.ParseResult;
        base.Execute(pipelineResult);
    }

    private void SetValue<T>(CliSymbol symbol, object? value)
    {
        cachedValues[symbol] = value;
    }

    private bool TryGetValue<T>(CliSymbol symbol, out T? value)
    {
        if (cachedValues.TryGetValue(symbol, out var objectValue))
        {
            value = objectValue is null
                ? default
                : (T)objectValue;
            return true;
        }
        value = default;
        return false;
    }

    public T? GetValue<T>(CliOption option)
        => GetValueInternal<T>(option);
    public T? GetValue<T>(CliArgument argument)
        => GetValueInternal<T>(argument);

    private T? GetValueInternal<T>(CliSymbol? symbol)
    {
        // NOTE: We use the subsystem's TryGetAnnotation here instead of the GetDefaultValue etc
        // extension methods, as the subsystem's TryGetAnnotation respects its annotation provider
        return symbol switch
        {
            not null when TryGetValue<T>(symbol, out var value)
                => value, // It has already been retrieved at least once
            CliArgument argument when parseResult?.GetValueResult(argument) is { } valueResult  // GetValue not used because it  would always return a value
                => UseValue(symbol, valueResult.GetValue<T>()), // Value was supplied during parsing, 
            CliOption option when parseResult?.GetValueResult(option) is { } valueResult  // GetValue not used because it would always return a value
                => UseValue(symbol, valueResult.GetValue<T>()), // Value was supplied during parsing
            // Value was not supplied during parsing, determine default now
            // configuration values go here in precedence
            //not null when GetDefaultFromEnvironmentVariable<T>(symbol, out var envName)
            //    => UseValue(symbol, GetEnvByName(envName)),
            not null when TryGetAnnotation(symbol, ValueAnnotations.DefaultValueCalculation, out Func<T?>? defaultValueCalculation)
                => UseValue(symbol, CalculatedDefault<T>(symbol, (Func<T?>)defaultValueCalculation)),
            not null when TryGetAnnotation(symbol, ValueAnnotations.DefaultValue, out T? explicitValue)
                => UseValue(symbol, explicitValue),
            null => throw new ArgumentNullException(nameof(symbol)),
            _ => UseValue(symbol, default(T))
        };

        TValue? UseValue<TValue>(CliSymbol symbol, TValue? value)
        {
            SetValue<TValue>(symbol, value);
            return value;
        }
    }

    private static T? CalculatedDefault<T>(CliSymbol symbol, Func<T?> defaultValueCalculation)
    {
        var objectValue = defaultValueCalculation();
        var value = objectValue is null
            ? default
            : (T)objectValue;
        return value;
    }
}